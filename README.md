[![Stargazers][stars-shield]][stars-url]

<h1 align="center">
  <br>
  <a href="https://github.com/stetze/RDS-Shadow"><img src="RDS-Shadow/Assets/Wide310x150Logo.scale-100.png" alt="Logo" ="200"></a>
  <br>
  RDS-Shadow
  <br>
</h1>

Made with <a href="https://github.com/microsoft/TemplateStudio">Microsoft Template Studio</a>

## Pre-requisites

<b>1. Active Directory</b><br>
Create an AD-Group "Domain\RDS-Shadow"<br>
Add Users in "Domain\RDS-Shadow"

<b>2. Configure the database for the Connection Broker</b>
```
USE [master]
GO
CREATE LOGIN [Domain\RDS-Shadow] FROM WINDOWS WITH DEFAULT_DATABASE=[RDSFARM]
GO
USE [RDSFARM]
GO
CREATE USER [Domain\RDS-Shadow] FOR LOGIN [Domain\RDS-Shadow]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[Shadowing]
AS
SELECT Session.UserName, Pool.DisplayName AS PoolName, Target.Name AS ServerName, Session.SessionId
FROM rds.Session AS Session
INNER JOIN rds.Target AS Target ON Target.Id = Session.TargetId
INNER JOIN rds.Pool AS Pool ON Target.PoolId = Pool.Id
WHERE (Session.State = 0) OR (Session.State = 1)
GO
GRANT SELECT ON [dbo].[Shadowing] TO [Domain\RDS-Shadow]
GO
```
<b>3. Add the Group (Domain\RDS-Shadow) to the role db_datareader</b>
```
ALTER ROLE db_datareader ADD MEMBER [Domain\RDS-Shadow]
```
<b>4. Add rights to Terminalserver</b>
```
wmic /namespace:\\root\CIMV2\TerminalServices PATH Win32_TSPermissionsSetting WHERE (TerminalName ="RDP-Tcp") CALL AddAccount "domain\rds-shadow",2
```

<a href="https://apps.microsoft.com/detail/9nlqv1vwwclc?hl=de-de&gl=DE?mode=direct“>
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://www.markdownguide.org/basic-syntax/#reference-style-links -->

[stars-shield]: https://img.shields.io/github/stars/stetze/RDS-Shadow.svg?style=for-the-badge
[stars-url]: https://github.com/stetze/RDS-Shadow/stargazers
