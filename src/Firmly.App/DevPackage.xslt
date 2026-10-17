<?xml version="1.0" encoding="utf-8"?>
<!--
  Turns Package.appxmanifest into the manifest for builds that don't ship the service (see Firmly.App.csproj):
  drops the service, and renames the package to "Firmly Dev" so it can be registered next to an installed Firmly.
-->
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:f="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
  xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">

  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>

  <xsl:template match="f:Identity/@Name">
    <xsl:attribute name="Name"><xsl:value-of select="concat(., '.Dev')" /></xsl:attribute>
  </xsl:template>

  <xsl:template match="f:Properties/f:DisplayName/text()">
    <xsl:value-of select="concat(., ' Dev')" />
  </xsl:template>

  <xsl:template match="uap:VisualElements/@DisplayName | uap5:StartupTask/@DisplayName">
    <xsl:attribute name="DisplayName"><xsl:value-of select="concat(., ' Dev')" /></xsl:attribute>
  </xsl:template>

  <!-- A COM class id can belong to one installed package only. -->
  <xsl:template match="desktop:ToastNotificationActivation/@ToastActivatorCLSID | com:Class/@Id">
    <xsl:attribute name="{name()}">8A9084D9-D024-4DD9-8B88-228314245D9E</xsl:attribute>
  </xsl:template>

  <xsl:template match="desktop6:Extension[@Category='windows.service']" />
  <xsl:template match="rescap:Capability[@Name='packagedServices' or @Name='localSystemServices']" />

  <!-- An empty Extensions element is invalid, so drop it when the service was its only entry. -->
  <xsl:template match="f:Extensions[not(*[not(self::desktop6:Extension[@Category='windows.service'])])]" />
</xsl:stylesheet>
