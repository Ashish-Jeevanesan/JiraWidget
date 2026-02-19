# JiraWidget

A proof-of-concept desktop widget for Windows that provides an 'always-on-top', at-a-glance view of a single Jira ticket's status.

## 🚀 About

This project is a UI/UX prototype for a lightweight, convenient desktop utility. The goal is to have a small, non-interactive window that always floats above other applications, allowing a user to constantly monitor the progress of a specific Jira ticket without needing to switch to a web browser.

The application window is configured to be:
- Small (300x120 pixels)
- Frameless (no title bar or borders)
- Non-resizable
- Always on top of other windows

**Note:** This is currently a visual prototype. The UI displays hardcoded placeholder data. There is no backend logic implemented to connect to the Jira API.

## ✨ Features

- **Floating Widget:** Displays as a small overlay on your desktop.
- **Visual Progress:** Includes a progress bar to represent ticket status.
- **At-a-Glance Info:** Shows the Jira ticket ID.

## 💻 Tech Stack

- **Language:** C#
- **UI Framework:** WinUI 3
- **Platform:** .NET 8
- **SDK:** Windows App SDK
- **Packaging:** MSIX for modern Windows deployment

## 🛠️ Prerequisites

To build and run this project, you will need:

- **Visual Studio:** With the **.NET Multi-platform App UI development** workload installed.
- **.NET 8.0 SDK**
- **Windows SDK:** Version 10.0.19041.0 or higher.

## ⚙️ Getting Started

1.  **Clone the repository.**
2.  **Open the solution:** Open the `JiraWidget.sln` file in Visual Studio.
3.  **Build the project:** Build the solution by pressing `Ctrl+Shift+B` or selecting `Build > Build Solution` from the menu.
4.  **Run the application:** Press `F5` or click the Start button in Visual Studio to launch the widget.

## 📈 Future Development

To turn this prototype into a functional application, the following steps are needed:

1.  **Jira API Integration:** Implement a service to connect to the Jira REST API using `HttpClient` or a dedicated client library.
2.  **Configuration:** Create a settings panel or configuration file where the user can input:
    - Their Jira instance URL.
    - An API token for authentication.
    - The specific Jira ticket ID to monitor.
3.  **Data Binding:** Replace the hardcoded values in `MainWindow.xaml` with data bindings to a view model that holds the live data from the API.
4.  **Auto-Refresh:** Implement a timer to periodically fetch the latest ticket status from the Jira API.
