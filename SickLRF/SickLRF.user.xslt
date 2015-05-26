<?xml version="1.0" encoding="utf-8"?>
<!-- 
    This file is part of Microsoft Robotics Developer Studio Code Samples.
    Copyright (C) Microsoft Corporation.  All rights reserved.
    $File: SickLRF.user.xslt $ $Revision: 8 $
-->
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:s="http://www.w3.org/2003/05/soap-envelope"
    xmlns:sicklrf="http://schemas.microsoft.com/xw/2005/12/sicklrf.user.html">

  <xsl:import href="/resources/dss/Microsoft.Dss.Runtime.Home.MasterPage.xslt" />

  <xsl:template match="/">
    <xsl:call-template name="MasterPage">
      <xsl:with-param name="serviceName">
        Sick Laser Range Finder
      </xsl:with-param>
      <xsl:with-param name="description">
        Sick Laser Range Finder LMS2xx
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="s:Header">

  </xsl:template>

  <xsl:template match="sicklrf:State">
    <table width="100%" align="center">
      <tr>
        <th colspan="2">
          Sick Laser Range Finder Configuration
        </th>
      </tr>
      <tr>
        <th width="20%">
          Angular Range:
        </th>
        <td>
          <xsl:value-of select="sicklrf:AngularRange"/>
        </td>
      </tr>
      <tr class="odd" width="80%">
        <th>
          Units:
        </th>
        <td>
          <xsl:value-of select="sicklrf:Units"/>
        </td>
      </tr>
      <tr>
        <th>
          Angular Resolution:
        </th>
        <td>
          <xsl:value-of select="sicklrf:AngularResolution"/>
        </td>
      </tr>
      <tr class="odd">
        <th>
          Time Stamp:
        </th>
        <td>
          <xsl:value-of select="sicklrf:TimeStamp"/>
        </td>
      </tr>
    </table>
</xsl:template>

</xsl:stylesheet>
