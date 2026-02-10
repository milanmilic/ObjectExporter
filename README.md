# ObjectExporter — Visual Studio Debug Object Exporter (JSON & C#)

Export any debug‑time object into **JSON** or **C#** with a single right‑click.  
ObjectExporter enhances Visual Studio debugging by instantly generating JSON views or C# schema representations of any object when execution is paused at a breakpoint.

> **Next Update:** Right‑click export from **Autos / Locals / Watch** panels.

---

## ✨ Features

- **Right‑click export while debugging**  
  When the debugger is paused, right‑click any object identifier → **ObjectExporter** → **Export to JSON** or **Export to C#**.

- **Instant editor tabs**  
  Generated files open in new tabs with proper syntax highlighting and the correct extension (`.json` or `.cs`).

- **JSON Tree View**  
  Displays a fully structured JSON representation with all nested objects and real runtime values.

- **C# Schema Generation**  
  Produces a complete class structure based on the inspected object, including nested types and a populated object snapshot.

- **Editable & Savable**  
  Exported tabs behave as regular files: save, rename, edit, and commit as needed.

---

## 🔧 Requirements

- Visual Studio 2022 or newer  
- .NET managed debugging  
- A running debug session paused at a breakpoint

---

## 📦 Installation

1. Install from the Visual Studio Marketplace (recommended).  
2. Or clone this repository and build the VSIX manually.  
3. Run the `.vsix` and restart Visual Studio if prompted.

---

## 🚀 Quick Start

1. Set a breakpoint in your code  
2. Start debugging  
3. When paused, right‑click on an object in the source editor  
4. Select **ObjectExporter** → **Export to JSON** or **Export to C#**  
5. A new tab opens with exported content  
6. Save or rename the file as desired

---

## 🖼️ Screenshots
### Exported File Tabs  
![ObjectExporter add-on](Assets/Screenshot.png)

---

## 🧭 How It Works

- Reads the current value of the selected object through the Visual Studio debugger  
- Traverses the object graph safely (cycle‑aware, preserves visited tracking)  
- Generates either:
  - structured JSON  
  - inferred C# class definitions
- Opens the generated text in a new Visual Studio editor window for editing and saving

---

## 🧪 Exapmle C# Export

```
// Schema
public sealed class Order {
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public User Owner { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class User {
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Roles { get; set; } = new();
}

public sealed class Item {
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
    public decimal Price { get; set; }
}

// Snapshot (example object instance)
var order = new Order {
    Id = 42,
    Title = "Sample",
    Owner = new User {
        UserId = "a1b2c3",
        Name = "Ada",
        Roles = new() { "admin", "editor" }
    },
    Items = new() {
        new Item { Sku = "X-100", Qty = 2, Price = 9.99m },
        new Item { Sku = "Y-200", Qty = 1, Price = 19.50m }
    },
    CreatedAt = DateTimeOffset.Parse("2026-02-10T08:00:00Z")
};
```

---

## 🧪 Example JSON Export

```json
{
  "id": 42,
  "title": "Sample",
  "owner": {
    "userId": "a1b2c3",
    "name": "Ada",
    "roles": ["admin", "editor"]
  },
  "items": [
    { "sku": "X-100", "qty": 2, "price": 9.99 },
    { "sku": "Y-200", "qty": 1, "price": 19.5 }
  ],
  "createdAt": "2026-02-10T08:00:00Z"
}
```
## ⚙️ Options & Behavior

- JSON → .json
- C# → .cs
- Supports nested objects, lists, dictionaries, primitives
- Prevents infinite loops from circular references
- Very large graphs may be truncated for responsiveness

---

## 🧩 Roadmap

- Export from Autos / Locals / Watch
- JSON formatting settings
- Depth limits
- Partial export (selected fields only)
- Custom converters

---

## ❓ FAQ
- Does exporting modify the running app?
- No — data is retrieved read‑only.
- Does generated C# always compile?
- Usually yes — dynamic patterns may need tweaks.
- Are private fields included?
- If the debugger can access them, yes.

---

## 🛠️ Development

- Open solution in Visual Studio 2022 +
- Set VSIX project as Startup Project
- Build (Release)
- .vsix appears in bin/Release/
- Install into the Experimental Instance

---

## 🧰 Testing Tips

- Test nested objects
- Validate JSON
- Test deep/hard objects
- Confirm saved files reload correctly

---

## 🐞 Troubleshooting
Menu not showing

- Must be paused on breakpoint
- Right‑click the identifier, not whitespace
- Ensure extension is enabled under Manage Extensions

Export slow

- Try smaller objects
- Check Output window

---

## 🔐 Privacy & Security

- No external communication
- All generation is local
- Be mindful when exporting sensitive data

---

## 🤝 Contributing

- Open an Issue
- Fork the repo
- Create a feature branch
- Submit a PR

---

## 🧾 License
- Include your license (MIT recommended).

---

## 🙏 Acknowledgments
- Thanks to the Visual Studio Extensibility community for testing and feedback.
