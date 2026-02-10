🧩 ObjectExporter – A Visual Studio Debug Extension for Exporting Objects as JSON or C#
ObjectExporter is a Visual Studio VSIX extension that allows developers to export any object inspected during a paused debug session into either JSON or C# format with a simple right‑click. Designed to streamline debugging, reverse engineering, API modeling, and data analysis, ObjectExporter provides an instant, structured representation of complex objects without leaving the IDE.

✨ Key Features
✔ Right‑Click Export on Any Object During Debugging
When the debugger is paused at a breakpoint, right‑click any object in your code editor and choose:
Export to JSON

Opens a new editor tab containing a fully structured JSON tree view
Displays all properties, nested objects, and values
Automatically applies the .json file extension
The generated file can be freely saved, renamed, or modified

Export to C#

Generates a complete C# class schema for the selected object
Includes all sub‑classes, nested types, and hierarchical structures
Shows a fully populated object instance below the schema
The tab is created with a .cs extension for easy saving or refactoring


🧭 Designed for Real Developer Workflows
ObjectExporter is ideal for:

Inspecting and analyzing complex objects
Converting runtime objects into reusable C# models
Extracting sample JSON payloads for API design or documentation
Generating test data and mocks
Reverse engineering responses, DTOs, and nested structures

All exported files behave like standard Visual Studio documents, allowing editing, syntax highlighting, saving, and version control.

🚧 Upcoming Features (Next Release)
The upcoming version will introduce full support for:
⭐ Right‑Click Export from Autos / Locals / Watch Panels
You will be able to export objects directly from Visual Studio’s debugging tool windows — without needing to reference them in source code. The same Export to JSON and Export to C# options will be available right where developers inspect variables the most.

📦 Installation
ObjectExporter can be installed from the Visual Studio Marketplace or by loading the VSIX file manually.
