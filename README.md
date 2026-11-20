# [CoffeeMC]

[![.NET](https://shields.io)](https://microsoft.com)
[![Avalonia UI](https://shields.io)](https://avaloniaui.net)
[![License](https://shields.io)](LICENSE)

🇷🇺 **[Читать на русском](README.ru.md)**

A lightweight, cross-platform Minecraft launcher built with **C#**, **Avalonia UI**, and **.NET 10**. Designed as a modern, native-feeling alternative without heavy dependencies like Qt.

![Screenshot Placeholder](assets/screenshot.png)

## ✨ Features

* **Cross-Platform:** Runs natively on Windows, Linux, and macOS thanks to Avalonia UI.
* **High Performance:** Compiled with .NET 10 for fast startup and low memory footprint.
* **Authentication:** [Microsoft Account / Offline mode / Ely.by — укажи нужное]
* **Version Management:** Easy installation and launching of Vanilla, Forge, Fabric, and NeoForge versions.
* **Modpack & Instance Support:** [Опиши кратко, если есть поддержка сборок или раздельных папок]

## 🚀 Getting Started

### Prerequisites
* [.NET 10 SDK](https://microsoft.comdownload/dotnet/10.0) (for building from source)
* Java 8 / 17 / 21 (depending on the Minecraft version you want to play)

### Installation (Release)
1. Go to the [Releases](../../releases) page.
2. Download the archive for your OS (`win-x64`).
3. Extract and run the executable.

### Building from Source
```bash
git clone https://github.com[username]/[repo-name].git
cd [repo-name]
dotnet run
```

To publish a standalone executable:
```bash
dotnet publish -r win-x64 -c Release /p:PublishSingleFile=true --self-contained true /p:IncludeNativeLibrariesForSelfContained=true
```

## 🛠️ Tech Stack

* **Language:** C# 14
* **Framework:** .NET 10
* **UI Framework:** Avalonia UI

## 🤝 Contributing

Contributions, issues, and feature requests are welcome! Feel free to check the [issues page](../../issues).

## 📝 License

This project is licensed under the [MIT License](LICENSE).

---
*Disclaimer: Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.*
