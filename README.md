ObjectExporter — Visual Studio Debug Object Exporter (JSON & C#)
Export any object at a breakpoint to JSON or C# with a single right‑click.
ObjectExporter is a Visual Studio VSIX extension that opens the exported content in a new editor tab, with the correct file extension (.json or .cs), ready to inspect, edit, save, or commit.

Coming next: Right‑click export directly from Autos / Locals / Watch windows for even faster workflows.


✨ Features


Context‑menu export while debugging
Right‑click any object in source code when the debugger is paused → ObjectExporter → Export to JSON / Export to C#.


Instant editor tabs
Results open immediately in new tabs with proper syntax highlighting and file extensions.


Rich JSON view
A navigable tree that includes all fields, properties, and nested objects—ideal for API payloads, samples, and debugging artifacts.


C# schema generation
A complete C# class model for the selected object (including nested types), plus a fully populated instance snapshot underneath.


Save & rename
Treat generated tabs like normal files: save to disk, rename, add to source control.



🔧 Requirements

Visual Studio 2022 or newer (Community, Professional, or Enterprise)
.NET projects where the Visual Studio managed debugger can inspect objects
A debug session paused at a breakpoint (or with execution otherwise suspended)


The extension works for any object the Visual Studio debugger can evaluate at the current scope.


📦 Installation

Install from Visual Studio Marketplace (recommended).
Or build the VSIX locally and double‑click the generated .vsix file.
Restart Visual Studio if prompted.


After installation, no configuration is required.


🚀 Quick Start

Set a breakpoint in your code and start debugging.
When execution pauses, right‑click the object (identifier) you want to export in the editor.
Choose ObjectExporter → Export to JSON or Export to C#.
A new tab opens with the exported content (.json or .cs).
Save the file (Ctrl+S) and optionally rename it.


🖼️ Screenshots

Replace the placeholders with your real images in the assets/ folder.



JSON tree view



C# schema & populated instance



Editor tab with correct extension




🧭 How It Works (High Level)

The extension asks the debugger for the current value of the selected symbol when paused.
The object graph is walked safely (handling cycles and repeated references).
For JSON, a normalized representation is produced (including all nested members).
For C#, types are inferred and a class model is generated (root + nested types), followed by a materialized instance section for quick copy/paste into tests.
The result is opened in a new document tab with the correct content type and extension for native Visual Studio features (syntax highlighting, formatting, save, etc.).
