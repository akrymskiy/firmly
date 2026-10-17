<?xml version="1.0" encoding="utf-8"?>
<!--
  Turns Package.appxmanifest into the manifest for builds that don't ship the service (see Firmly.App.csproj):
  drops the service, and renames the package to "Firmly Dev" so it can be registered next to an installed Firmly.
-->
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:f="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
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

  <xsl:template match="uap:VisualElements/@DisplayName">
    <xsl:attribute name="DisplayName"><xsl:value-of select="concat(., ' Dev')" /></xsl:attribute>
  </xsl:template>

  <xsl:template match="desktop6:Extension[@Category='windows.service']" />
  <xsl:template match="rescap:Capability[@Name='packagedServices' or @Name='localSystemServices']" />

  <!-- An empty Extensions element is invalid, so drop it when the service was its only entry. -->
  <xsl:template match="f:Extensions[not(*[not(self::desktop6:Extension[@Category='windows.service'])])]" />
</xsl:stylesheet>
