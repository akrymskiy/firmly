<?xml version="1.0" encoding="utf-8"?>
<!-- Removes the packaged service from Package.appxmanifest for builds that don't ship it; see Firmly.App.csproj. -->
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:f="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">

  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>

  <xsl:template match="desktop6:Extension[@Category='windows.service']" />
  <xsl:template match="rescap:Capability[@Name='packagedServices' or @Name='localSystemServices']" />

  <!-- An empty Extensions element is invalid, so drop it when the service was its only entry. -->
  <xsl:template match="f:Extensions[not(*[not(self::desktop6:Extension[@Category='windows.service'])])]" />
</xsl:stylesheet>
