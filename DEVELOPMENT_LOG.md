# JiraWidget Development Log

This document summarizes the development and debugging journey of the JiraWidget application.

## Current Application Features:
-   **Multi-Issue Tracking:** Users can add multiple Jira issues (PCs) to a list for simultaneous tracking.
-   **Dynamic UI:** The widget dynamically expands/contracts vertically as issues are added/removed.
-   **Input Validation:** Issue keys are validated to ensure they conform to the `PC-XXXXX` format.
-   **Remove Issues:** Each tracked issue has a button to remove it from the list.
-   **Separate Login/Main Views:** Users log in once, then interact with a main view to track issues.
-   **Custom Progress Calculation:** Progress is calculated based on "Activities" linked issues having a "Done" status.
-   **Dynamic Error Display:** The UI provides detailed error messages when API calls fail.

## Authentication and API Debugging Journey:

### Initial Setup:
-   The application was initially set up to use Jira Cloud-style PAT (Personal Access Token) with `Bearer` authentication.
-   The API calls were made using `GET` to `/rest/api/3/issue/{issueKey}` for issue details and `/rest/api/3/myself` for connection testing.

### On-Premise Jira Server / Okta Authentication Shift:
-   User confirmed usage of an **on-premise Jira Server** with **Okta authentication**.
-   The strategy shifted to using PATs for a non-browser client.

### Debugging API Call Failures:

1.  **Issue 1: `GET /issue/{key}` -> `405 Method Not Allowed`**
    -   **Cause:** Suspected network/proxy blocking `GET` requests for the Jira API.
    -   **Workaround Attempt:** Changed `GetIssueAsync` to use `POST` to `/rest/api/2/issue/{issueKey}` with an empty JSON body.

2.  **Issue 2: `POST /issue/{key}` (empty body) -> `500 Internal Server Error`**
    -   **Cause:** The Jira server received the `POST` request but did not like the empty body, leading to an internal crash.
    -   **Workaround Attempt:** Changed `POST` body to `{"fields":["key", "issuelinks"]}`.

3.  **Issue 3: `POST /issue/{key}` (with fields body) -> `400 Bad Request`**
    -   **Cause:** The Jira server received the `POST` request, but the payload's format (specifically `fields` parameter) was incorrect for this endpoint when using `POST` to retrieve data.
    -   **Workaround Attempt:** Switched to `POST /rest/api/2/search` endpoint.

4.  **Issue 4: `POST /search` (JQL only, no fields) -> `200 OK`, but `0/0 Done`**
    -   **Cause:** This was a breakthrough. The connection worked! However, because no fields were explicitly requested, the returned issue object did not contain `issuelinks` data, resulting in 0% progress.
    -   **Workaround Attempt:** Re-introduced the `fields` parameter to `POST /search` body: `{"jql":"key='PC-...'","fields":["summary","status","issuelinks"]}`.

5.  **Issue 5: `POST /search` (JQL + fields) -> `400 Bad Request` (again)**
    -   **Cause:** Even the standard `/search` endpoint was rejecting the `fields` parameter when sent in a `POST` body. This indicated a fundamental non-standard behavior or strict configuration on the Jira server for processing the `fields` parameter in a POST body.

## Current State & Next Steps:

-   The browser works because it leverages existing user session cookies (Okta login).
-   The application is struggling with non-standard API behavior and network restrictions for programmatic access.
-   **Current Plan:** Revert to Basic Authentication. The new theory is that the Jira server expects the PAT to be used as a password with Basic Authentication, and this approach might also allow standard `GET` requests to work, bypassing the proxy issues and the `/search` endpoint's `fields` parameter problem.

This log will be updated as development continues.

## Recent Updates:

-   **2026-02-20:** Added a shared `ValidateAndEnterMainViewAsync` helper to consolidate login flow validation, and fixed the main view default size so the widget opens at the 3-item height before any issues are tracked.
