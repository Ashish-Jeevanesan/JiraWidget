# JiraWidget

JiraWidget is a WinUI 3 desktop utility that stays on top of other windows and tracks progress for multiple Jira issues in a compact widget view.

## Overview

This project is no longer a static UI prototype. It includes:
- Jira API integration through `HttpClient`
- Login support for PAT (Bearer) and Okta browser-based session flow
- Multi-issue tracking with per-issue progress bars
- Favorites with local persistence and auto-load after login
- Clickable issue keys that open Jira in your default browser
- Dynamic resizing based on tracked issue count and content width
- Window-level opacity control from a title-bar menu
- Local file logging for diagnostics

## Current Features

- Always-on-top floating window with custom title bar controls
- Title bar menu (`☰`) with real-time transparency slider
- Login view and tracking view in a single window
- Track multiple issue keys (`PC-12345` format validation)
- Press `Enter` in issue input to trigger tracking
- Remove tracked issues individually
- Favorite/unfavorite tracked issues (`★` / `☆`)
- Favorite indicator bar on the left edge of favorited rows
- Per-issue retry button for failed/refresh attempts
- Auto-load favorite issues after successful login
- Remove from favorites automatically when removed from tracked list
- Jira API version fallback strategy (`/rest/api/3` then `/rest/api/2`)
- Progress calculation rules:
  - Base source: `Activities` linked issues
  - Exclude linked issues whose key starts with `TSK-`
  - Include subtasks whose summary matches `PM Approval` or `Post-Production Verification`
  - Done state: status name equals `Done`

## Tech Stack

- C#
- .NET 8 (`net8.0-windows10.0.19041.0`)
- WinUI 3 (Windows App SDK)
- MSIX packaging support

## Prerequisites

- Windows 10/11 development environment
- Visual Studio 2022 with Windows App SDK / WinUI workload
- .NET 8 SDK
- Windows SDK 10.0.19041.0 or newer

## Build and Run

1. Clone the repository.
2. Open `JiraWidget.slnx` in Visual Studio.
3. Restore NuGet packages.
4. Build and run (`F5`).

## Authentication Modes

- PAT mode:
  - Uses `Authorization: Bearer <token>`
  - Validates with `/rest/api/{version}/myself`
- Okta mode:
  - Uses embedded WebView2 login
  - Captures session cookies (`JSESSIONID`, `seraph.rememberme.cookie`, etc.)
  - Reuses those cookies for Jira API calls

## Logging and Diagnostics

- Application logs are written to:
  - `%LocalAppData%\\JiraWidget\\jira-widget.log`
- In packaged runs, this typically resolves under:
  - `%LocalAppData%\\Packages\\<package-id>\\LocalCache\\Local\\JiraWidget\\jira-widget.log`
- Common observed behavior in current environments:
  - `/rest/api/3` may return HTML/404 or redirects
  - `/rest/api/2` may still succeed
- Progress logs include detailed included/excluded activities and included subtasks.

## Known Limitations

- No background auto-refresh timer yet; issue data is fetched when an issue is added.
- Jira URL/token are not persisted across sessions.
- Progress logic is tailored to Jira setups using `Activities` links and current naming conventions.
- Behavior depends on Jira server/network/SSO configuration, especially for on-prem + Okta flows.

## Project Structure

- `JiraWidget/MainWindow.xaml` and `MainWindow.xaml.cs`: UI and app flow
- `JiraWidget/JiraService.cs`: Jira authentication and API calls
- `JiraWidget/JiraModels.cs`: JSON models
- `JiraWidget/FavoritesStore.cs`: local favorites persistence
- `JiraWidget/TrackedIssueViewModel.cs`: bindable tracked issue state
- `JiraWidget/AppLogger.cs`: file logger

## Next Improvements

- Add periodic refresh with configurable interval
- Persist additional user settings (Jira URL, opacity)
- Add clearer per-issue API error states in UI
- Add unit tests for parsing/progress logic
