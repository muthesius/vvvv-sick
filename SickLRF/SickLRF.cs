//-----------------------------------------------------------------------
//  This file is part of Microsoft Robotics Developer Studio Code Samples.
//
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//
//  $File: SickLRF.cs $ $Revision: 25 $
//-----------------------------------------------------------------------

using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;


using Microsoft.Ccr.Core;
using Microsoft.Dss.Core;
using Microsoft.Dss.Core.Attributes;
using Microsoft.Dss.Services.Serializer;

using Microsoft.Dss.ServiceModel.Dssp;
using submgr = Microsoft.Dss.Services.SubscriptionManager;
using Microsoft.Dss.ServiceModel.DsspServiceBase;
using Microsoft.Dss.Core.DsspHttp;
using Microsoft.Dss.Core.DsspHttpUtilities;
using System.Net;
using System.Net.Mime;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.ComponentModel;
using W3C.Soap;

namespace Microsoft.Robotics.Services.Sensors.SickLRF
{
    /// <summary>
    /// Sick Laser Range Finder service.
    /// </summary>
    [Contract(Contract.Identifier)]
    [DisplayName("(User) Sick Laser Range Finder")]
    [Description("Provides access to a Sick Laser Range Finder LMS2xx.")]
    [DssServiceDescription("http://msdn.microsoft.com/library/cc998493.aspx")]
    public class SickLRFService : DsspServiceBase
    {
        CommLink _link;
        LRFCommLinkPort _internalPort = new LRFCommLinkPort();

        [ServicePort("/sicklrf")]
        SickLRFOperations _mainPort = new SickLRFOperations();

        [ServiceState(StateTransform = "Microsoft.Robotics.Services.Sensors.SickLRF.SickLRF.user.xslt")]
        [InitialStatePartner(Optional = true, ServiceUri = ServicePaths.Store + "/SickLRF.config.xml")]
        State _state = new State();

        // This is no longer used - Use base.StateTransformPath instead (see above)
        //[EmbeddedResource("Microsoft.Robotics.Services.Sensors.SickLRF.SickLRF.user.xslt")]
        //string _transform = null;

        [Partner("SubMgr", Contract = submgr.Contract.Identifier, CreationPolicy = PartnerCreationPolicy.CreateAlways)]
        submgr.SubscriptionManagerPort _subMgrPort = new submgr.SubscriptionManagerPort();

        DsspHttpUtilitiesPort _httpUtilities;

        DispatcherQueue _queue = null;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="creationPort">Passed to the base class for construction.</param>
        public SickLRFService(DsspServiceCreationPort creationPort) :
                base(creationPort)
        {
        }

        /// <summary>
        /// Send phase of construction.
        /// </summary>
        protected override void Start()
        {
            if (_state == null)
            {
                _state = new State();
            }

            LogInfo("Start");

            _httpUtilities = DsspHttpUtilitiesService.Create(Environment);

            //
            // Kick off the connection to the Laser Range Finder device.
            //
            SpawnIterator(0, _state.ComPort, StartLRF);

            // This service does not use base.Start() because of the way that
            // the handlers are hooked up. Also, because of this, there are
            // explicit Get and HttpGet handlers instead of using the default ones.
            // Handlers that need write or Exclusive access to state go under
            // the Exclusive group. Handlers that need read or shared access, and can be
            // Concurrent to other readers, go to the Concurrent group.
            // Other internal ports can be included in interleave so you can coordinate
            // intermediate computation with top level handlers.
            Activate(
                Arbiter.Interleave(
                new TeardownReceiverGroup(
                    Arbiter.Receive<DsspDefaultDrop>(false, _mainPort, DropHandler)),
                new ExclusiveReceiverGroup(
                    Arbiter.Receive<Replace>(true, _mainPort, ReplaceHandler),
                    Arbiter.Receive<LinkMeasurement>(true, _internalPort, MeasurementHandler),
                    Arbiter.ReceiveWithIterator<LinkPowerOn>(true, _internalPort, PowerOn),
                    Arbiter.ReceiveWithIterator<Exception>(true, _internalPort, ExceptionHandler)),
                new ConcurrentReceiverGroup(
                    Arbiter.Receive<DsspDefaultLookup>(true, _mainPort, DefaultLookupHandler),
                    Arbiter.ReceiveWithIterator<Subscribe>(true, _mainPort, SubscribeHandler),
                    Arbiter.ReceiveWithIterator<ReliableSubscribe>(true, _mainPort, ReliableSubscribeHandler),
                    Arbiter.Receive<Get>(true, _mainPort, GetHandler),
                    Arbiter.Receive<HttpGet>(true, _mainPort, HttpGetHandler),
                    Arbiter.Receive<Reset>(true, _mainPort, ResetHandler))
                )
            );

            DirectoryInsert();
        }

        #region Initialization

        /// <summary>
        /// Start conversation with the SickLRF device.
        /// </summary>
        IEnumerator<ITask> StartLRF(int timeout, int comPort)
        {
            if (timeout > 0)
            {
                //
                // caller asked us to wait <timeout> milliseconds until we start.
                //

                yield return Arbiter.Receive(false, TimeoutPort(timeout),
                    delegate(DateTime dt)
                    {
                        LogInfo("Done Waiting");
                    }
                );
            }

            if (_queue == null)
            {
                //
                // The internal services run on their own dispatcher, we need to create that (once)
                //

                AllocateExecutionResource allocExecRes = new AllocateExecutionResource(0, "SickLRF");

                ResourceManagerPort.Post(allocExecRes);

                yield return Arbiter.Choice(
                    allocExecRes.Result,
                    delegate(ExecutionAllocationResult result)
                    {
                        _queue = result.TaskQueue;
                    },
                    delegate(Exception e)
                    {
                        LogError(e);
                    }
                );
            }

            string comName;

            if (comPort <= 0)
            {
                //
                // We default to COM3, because
                // a) that was our previous behavior and
                // b) the hardware that we have uses COM3
                //
                comName = "COM3";
            }
            else
            {
                comName = "COM" + comPort;
            }

            _link = new CommLink(_queue ?? TaskQueue, comName, _internalPort);
            _link.Parent = ServiceInfo.Service;
            _link.Console = ConsoleOutputPort;

            FlushPortSet(_internalPort);
            yield return(
                Arbiter.Choice(
                    _link.Open(),
                    delegate(SuccessResult success)
                    {
                        LogInfo("Opened link to LRF");
                    },
                    delegate(Exception exception)
                    {
                        LogError(exception);
                    }
                )
            );
        }

        private void FlushPortSet(IPortSet portSet)
        {
            foreach (IPortReceive port in portSet.Ports)
            {
                while (port.Test() != null) ;
            }
        }

        IEnumerator<ITask> PowerOn(LinkPowerOn powerOn)
        {
            bool failed = false;

            _state.Description = powerOn.Description;
            _state.LinkState = "Power On: " + powerOn.Description;
            LogInfo(_state.LinkState);

            //
            // the device has powered on. Set the BaudRate to the highest supported.
            //

            yield return Arbiter.Choice(
                _link.SetDataRate(38400),
                delegate(SuccessResult success)
                {
                    _state.LinkState = "Baud Rate set to " + 38400;
                    LogInfo(_state.LinkState);
                },
                delegate(Exception failure)
                {
                    _internalPort.Post(failure);
                    failed = true;
                }
            );

            if (failed)
            {
                yield break;
            }

            //
            // wait for confirm to indicate that the LRF has received the new baud rate and is
            // expecting the serial rate to change imminently.
            //

            yield return Arbiter.Choice(
                Arbiter.Receive<LinkConfirm>(false,_internalPort,
                    delegate(LinkConfirm confirm)
                    {
                        // the confirm indicates that the LRF has recieved the new baud rate
                    }),
                Arbiter.Receive<DateTime>(false, TimeoutPort(1000),
                    delegate(DateTime time)
                    {
                        _internalPort.Post(new TimeoutException("Timeout waiting for Confirm while setting data rate"));
                        failed = true;
                    })
            );

            if (failed)
            {
                yield break;
            }

            //
            // Set the serial rate to the rate requested above.
            //

            yield return Arbiter.Choice(
                _link.SetRate(),
                delegate(SuccessResult success)
                {
                    _state.LinkState = "Changed Rate to: " + _link.BaudRate;
                    LogInfo(_state.LinkState);
                },
                delegate(Exception failure)
                {
                    _internalPort.Post(failure);
                    failed = true;
                }
            );

            if (failed)
            {
                yield break;
            }

            //
            // start continuous measurements.
            //

            yield return Arbiter.Choice(
                _link.SetContinuous(),
                delegate(SuccessResult success)
                {
                    _state.LinkState = "Starting Continuous Measurement";
                    LogInfo(_state.LinkState);
                },
                delegate(Exception failure)
                {
                    _internalPort.Post(failure);
                    failed = true;
                }
            );

            if (failed)
            {
                yield break;
            }

            //
            // wait for confirm message that signals that the LRF is now in continuous measurement mode.
            //

            yield return Arbiter.Choice(
                Arbiter.Receive<LinkConfirm>(false, _internalPort,
                    delegate(LinkConfirm confirm)
                    {
                        // received Confirm
                    }),
                Arbiter.Receive<DateTime>(false, TimeoutPort(1000),
                    delegate(DateTime time)
                    {
                        _internalPort.Post(new TimeoutException("Timeout waiting for Confirm after setting continuous measurement mode"));
                    })
            );

            yield break;
        }


        #endregion

        #region Laser Range Finder events
        /// <summary>
        /// Handle new measurement data from the LRF.
        /// </summary>
        /// <param name="measurement">Measurement Data</param>
        void MeasurementHandler(LinkMeasurement measurement)
        {
            try
            {
                //
                // The SickLRF typically reports on either a 180 degrees or 100 degrees
                // field of vision. From the number of readings we can calculate the
                // Angular Range and Resolution.
                //
                switch (measurement.Ranges.Length)
                {
                    case 181:
                        _state.AngularRange = 180;
                        _state.AngularResolution = 1;
                        break;

                    case 361:
                        _state.AngularRange = 180;
                        _state.AngularResolution = 0.5;
                        break;

                    case 101:
                        _state.AngularRange = 100;
                        _state.AngularResolution = 1;
                        break;

                    case 201:
                        _state.AngularRange = 100;
                        _state.AngularResolution = 0.5;
                        break;

                    case 401:
                        _state.AngularRange = 100;
                        _state.AngularResolution = 0.25;
                        break;

                    default:
                        break;
                }
                _state.DistanceMeasurements = measurement.Ranges;
                _state.Units = measurement.Units;
                _state.TimeStamp = measurement.TimeStamp;
                _state.LinkState = "Measurement received";

                //
                // Inform subscribed services that the state has changed.
                //
                _subMgrPort.Post(new submgr.Submit(_state, DsspActions.ReplaceRequest));
            }
            catch (Exception e)
            {
                LogError(e);
            }

        }

        IEnumerator<ITask> ExceptionHandler(Exception exception)
        {
            LogError(exception);

            BadPacketException bpe = exception as BadPacketException;

            if (bpe != null && bpe.Count < 2)
            {
                yield break;
            }

            _subMgrPort.Post(new submgr.Submit(new ResetType(), DsspActions.SubmitRequest));

            LogInfo("Closing link to LRF");
            yield return
                Arbiter.Choice(
                    _link.Close(),
                    delegate(SuccessResult success)
                    {
                    },
                    delegate(Exception except)
                    {
                        LogError(except);
                    }
                );

            _state.LinkState = "LRF Link closed, waiting 5 seconds";
            LogInfo(_state.LinkState);
            _link = null;

            SpawnIterator(5000, _state.ComPort, StartLRF);

            yield break;
        }

        #endregion

        #region DSSP operation handlers

        void GetHandler(Get get)
        {
            get.ResponsePort.Post(_state);
        }


        void ReplaceHandler(Replace replace)
        {
            _state = replace.Body;
            replace.ResponsePort.Post(DefaultReplaceResponseType.Instance);
        }

        IEnumerator<ITask> SubscribeHandler(Subscribe subscribe)
        {
            yield return Arbiter.Choice(
                SubscribeHelper(_subMgrPort, subscribe.Body, subscribe.ResponsePort),
                delegate(SuccessResult success)
                {
                    if (_state != null &&
                        _state.DistanceMeasurements != null)
                    {
                        _subMgrPort.Post(new submgr.Submit(
                            subscribe.Body.Subscriber, DsspActions.ReplaceRequest, _state, null));
                    }
                },
                null
            );
        }

        IEnumerator<ITask> ReliableSubscribeHandler(ReliableSubscribe subscribe)
        {
            yield return Arbiter.Choice(
                SubscribeHelper(_subMgrPort, subscribe.Body, subscribe.ResponsePort),
                delegate(SuccessResult success)
                {
                    if (_state != null &&
                        _state.DistanceMeasurements != null)
                    {
                        _subMgrPort.Post(new submgr.Submit(
                            subscribe.Body.Subscriber, DsspActions.ReplaceRequest, _state, null));
                    }
                },
                null
            );
        }

        void DropHandler(DsspDefaultDrop drop)
        {
            try
            {
                if (_link != null)
                {
                    // release dispatcher queue resource
                    ResourceManagerPort.Post(new FreeExecutionResource(_link.TaskQueue));
                    _link.Close();
                    _link = null;
                }
            }
            finally
            {
                base.DefaultDropHandler(drop);
            }
        }

        void ResetHandler(Reset reset)
        {
            _internalPort.Post(new Exception("External Reset Requested"));
            reset.ResponsePort.Post(DefaultSubmitResponseType.Instance);
        }

        #endregion

        #region HttpGet Handlers

        static readonly string _root = "/sicklrf";
        static readonly string _cylinder = "/sicklrf/cylinder";
        static readonly string _top = "/sicklrf/top";
        static readonly string _topw = "/sicklrf/top/";


        void HttpGetHandler(HttpGet httpGet)
        {
            HttpListenerRequest request = httpGet.Body.Context.Request;
            HttpListenerResponse response = httpGet.Body.Context.Response;

            Stream image = null;

            string path = request.Url.AbsolutePath;

            if (path == _cylinder)
            {
                image = GenerateCylinder();
            }
            else if (path == _top)
            {
                image = GenerateTop(400);
            }
            else if (path.StartsWith(_topw))
            {
                int width;
                string remain = path.Substring(_topw.Length);

                if (int.TryParse(remain, out width))
                {
                    image = GenerateTop(width);
                }
            }
            else if (path == _root)
            {
                HttpResponseType rsp = new HttpResponseType(HttpStatusCode.OK,
                _state,
                base.StateTransformPath);
                httpGet.ResponsePort.Post(rsp);
            }



            if (image != null)
            {
                SendJpeg(httpGet.Body.Context, image);
            }
            else
            {
                httpGet.ResponsePort.Post(Fault.FromCodeSubcodeReason(
                    W3C.Soap.FaultCodes.Receiver,
                    DsspFaultCodes.OperationFailed,
                    "Unable to generate Image"));
            }
        }

        private void SendJpeg(HttpListenerContext context, Stream stream)
        {
            WriteResponseFromStream write = new WriteResponseFromStream(context, stream, MediaTypeNames.Image.Jpeg);

            _httpUtilities.Post(write);

            Activate(
                Arbiter.Choice(
                    write.ResultPort,
                    delegate(Stream res)
                    {
                        stream.Close();
                    },
                    delegate(Exception e)
                    {
                        stream.Close();
                        LogError(e);
                    }
                )
            );
        }

        #endregion

        #region Image generators

        private Stream GenerateCylinder()
        {
            if (_state.DistanceMeasurements == null)
            {
                return null;
            }

            MemoryStream memory = null;

            using (Bitmap bmp = new Bitmap(_state.DistanceMeasurements.Length, 100))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.LightGray);

                    int half = bmp.Height / 2;
                    int middle = _state.DistanceMeasurements.Length / 2;

                    for (int x = 0; x < _state.DistanceMeasurements.Length; x++)
                    {
                        int range = _state.DistanceMeasurements[x];

                        if (x == middle)
                        {
                            g.DrawLine(Pens.Gray, x, 0, x, 100);
                        }
                        if (range > 0 && range < 8192)
                        {
                            int h = bmp.Height * 500 / range;
                            if (h < 0)
                            {
                                h = 0;
                            }
                            Color col = LinearColor(Color.DarkBlue, Color.LightGray, 0, 8192, range);
                            g.DrawLine(new Pen(col), bmp.Width - x, half - h, bmp.Width - x, half + h);
                        }
                    }
                    g.DrawString(
                        _state.TimeStamp.ToString(),
                        new Font(FontFamily.GenericSansSerif, 10, GraphicsUnit.Pixel),
                        Brushes.Gray, 0, 0
                    );
                }

                memory = new MemoryStream();
                bmp.Save(memory, ImageFormat.Jpeg);
                memory.Position = 0;
            }

            return memory;
        }

        private Color LinearColor(Color nearColor, Color farColor, int nearLimit, int farLimit, int value)
        {
            if (value <= nearLimit)
            {
                return nearColor;
            }
            else if (value >= farLimit)
            {
                return farColor;
            }

            int span = farLimit - nearLimit;
            int pos = value - nearLimit;

            int r = (nearColor.R * (span - pos) + farColor.R * pos) / span;
            int g = (nearColor.G * (span - pos) + farColor.G * pos) / span;
            int b = (nearColor.B * (span - pos) + farColor.B * pos) / span;

            return Color.FromArgb(r, g, b);
        }

        private Stream GenerateTop(int width)
        {
            if (_state.DistanceMeasurements == null)
            {
                return null;
            }

            MemoryStream memory = null;

            int height = width / 2;
            using (Bitmap bmp = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.LightGray);

                    double angularOffset = -90 + _state.AngularRange / 2.0;
                    double piBy180 = Math.PI / 180.0;
                    double halfAngle = _state.AngularResolution / 2.0;
                    double scale = height / 8000.0;

                    GraphicsPath path = new GraphicsPath();

                    for (int pass = 0; pass != 2; pass++)
                    {
                        for (int i = 0; i < _state.DistanceMeasurements.Length; i++)
                        {
                            int range = _state.DistanceMeasurements[i];
                            if (range > 0 && range < 8192)
                            {
                                double angle = i * _state.AngularResolution - angularOffset;
                                double lowAngle = (angle - halfAngle) * piBy180;
                                double highAngle = (angle + halfAngle) * piBy180;

                                double drange = range * scale;

                                float lx = (float)(height + drange * Math.Cos(lowAngle));
                                float ly = (float)(height - drange * Math.Sin(lowAngle));
                                float hx = (float)(height + drange * Math.Cos(highAngle));
                                float hy = (float)(height - drange * Math.Sin(highAngle));

                                if (pass == 0)
                                {
                                    if (i == 0)
                                    {
                                        path.AddLine(height, height, lx, ly);
                                    }
                                    path.AddLine(lx, ly, hx, hy);
                                }
                                else
                                {
                                    g.DrawLine(Pens.DarkBlue, lx, ly, hx, hy);
                                }
                            }
                        }

                        if (pass == 0)
                        {
                            g.FillPath(Brushes.White, path);
                        }
                    }

                    float botWidth = (float)(190 * scale);
                    g.DrawLine(Pens.Red, height, height - botWidth, height, height);
                    g.DrawLine(Pens.Red, height - 3, height - botWidth, height + 3, height - botWidth);
                    g.DrawLine(Pens.Red, height - botWidth, height - 3, height - botWidth, height);
                    g.DrawLine(Pens.Red, height + botWidth, height - 3, height + botWidth, height);
                    g.DrawLine(Pens.Red, height - botWidth, height - 1, height + botWidth, height - 1);

                    g.DrawString(
                        _state.TimeStamp.ToString(),
                        new Font(FontFamily.GenericSansSerif, 10, GraphicsUnit.Pixel),
                        Brushes.Gray, 0, 0
                    );
                }

                memory = new MemoryStream();
                bmp.Save(memory, ImageFormat.Jpeg);
                memory.Position = 0;

            }
            return memory;
        }

        #endregion
    }
}
