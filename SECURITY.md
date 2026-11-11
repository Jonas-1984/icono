# Security

icono is a desktop program (Windows, .NET 8). It makes no network connections of its own, has no accounts, no telemetry and no background
services.

## Reporting a vulnerability

Please report security problems **confidentially** to **info@bitbins.de** (subject "icono security"). Do not open a public issue for it.
Please include the version (Help > About icono), the steps to reproduce it and, if you can, a sample file. You will get an answer as soon
as possible; a fix is released as a new installer.

## What is protected, and how

- **Files from outside are untrusted data.** SVG is parsed without a DTD and without entities (no "billion laughs", no reading of local files)
  and is limited to 20 MB; documents are limited to 512 MB and pictures to 64 MB.
- **Only fixed addresses are opened** (project page, bitbins.de, the contact mail address). Never an address from a document.
- **Only fixed local files are opened** by the About window and the Help menu (manual, test terms, privacy notice, third-party notices).
- **No network code** in the program; no scripts, macros or plug-ins are executed.
- **Dependencies** are checked for known vulnerabilities before every installer (`dotnet list package --vulnerable`); the shipped projects have none.
- **Installer:** built per user (no administrator rights), checksum (SHA-256) published next to it. The test installer is **not yet code-signed**.

## Supported versions

Only the newest test version receives fixes.
