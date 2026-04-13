# MiniLIS Suite

**MiniLIS Suite** is a modern, lightweight Laboratory Information System (LIS) specialized in flow cytometry and immunology data management. Built with **Blazor Server** and **.NET 9**, it provides a high-fidelity reporting engine, transactional sample registration, and a professional workspace for clinical analysis.

## 🚀 Key Features

- **Transactional Sample Registration**: Atomic registration of Patients, Requests, and Samples with automatic numbering (`YY-NNNN`).
- **Interactive Report Editor**: Professional UI to capture marker intensities and percentages with real-time text synthesis.
- **Configurable Medical Headers**: Admin-level control over clinical logos, header text, and alignments.
- **Multi-format Generation**: High-fidelity PDF reports (via QuestPDF) and editable ODT exports for LibreOffice.
- **Modern Infrastructure**: SQL Server backend with Entity Framework Core, optimistic concurrency, and automated audit logs.

## 🛠️ Tech Stack

- **Frontend**: Blazor Server (ASP.NET Core 9), Bootstrap 5, Bi-Icons.
- **Backend**: C#, Entity Framework Core (SQL Server).
- **Document Generation**: QuestPDF (PDF), OpenXML/ZIP manipulation (ODT).
- **Architecture**: Clean Architecture principles (Domain, Application, Infrastructure, Web).

## 📦 Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (LocalDB supported)

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/jda-git/MiniLIS-Suite.git
   ```

2. Build the solution:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   cd MiniLIS.Web
   dotnet run
   ```

## 📄 License

This project is for demonstration purposes. All rights reserved.

---
Developed with ❤️ by Antigravity AI Coding Assistant.
