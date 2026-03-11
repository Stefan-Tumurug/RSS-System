# RSS-System

## Overview

RSS-System is a multi-project .NET solution containing three components
that together demonstrate a simple RSS-based system architecture.

The repository is organized as a single Git repository that contains
three separate projects:

-   **RSSAPI** -- Backend API responsible for exposing RSS-related
    endpoints.
-   **RSSPlayer** -- A client/player application that consumes RSS
    feeds.
-   **RSS_Site** -- A web interface for interacting with the RSS system.

The projects are kept in the same repository to simplify development,
version control, and deployment.

------------------------------------------------------------------------

## Project Structure

    RSS-System
    ├─ RSSAPI
    │  ├─ RSSAPI.sln
    │  └─ RSSAPI/
    ├─ RSSPlayer
    │  ├─ RSSPlayer.sln
    │  └─ RSS_Player/
    ├─ RSS_Site
    │  ├─ RSSSite.sln
    │  └─ RSS-Site/
    └─ README.md

Each folder contains its own Visual Studio solution and project files.

------------------------------------------------------------------------

## Components

### RSSAPI

The API project provides backend functionality for working with RSS
data.

Typical responsibilities include: - Exposing API endpoints - Processing
RSS feeds - Providing data to clients

### RSSPlayer

RSSPlayer is a client application that consumes RSS feeds from the API
or other sources.
Features: - Fetching RSS feeds - Displaying feed content - Live Updates from API

### RSS_Site

The web project provides a browser-based interface for interacting with
the RSS system.

Features: - Viewing and Managing RSSPlayer feeds - Managing feed subscriptions - Interacting with the backend API

------------------------------------------------------------------------

## Development Setup

### Requirements

-   .NET SDK
-   Visual Studio or Visual Studio Code
-   Git

### Open a project

Each component has its own solution file. You can open them individually
in Visual Studio.

Examples:

    RSSAPI/RSSAPI.sln
    RSSPlayer/RSSPlayer.sln
    RSS_Site/RSSSite.sln

------------------------------------------------------------------------

## Git Notes

This repository intentionally keeps all projects inside a single Git
repository.

Important: - Nested `.git` repositories were removed so all projects are
tracked by the main repository. - Build files such as `bin`, `obj`, and
`.vs` should be ignored through `.gitignore`.

Typical `.gitignore` entries:

    .vs/
    **/bin/
    **/obj/
    *.user
------------------------------------------------------------------------

## License

This project is provided for educational and development purposes, and might not function as intended due to being reliant on a company server.
