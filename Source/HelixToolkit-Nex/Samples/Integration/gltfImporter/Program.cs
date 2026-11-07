using HelixToolkit.Nex.Sample.GltfImporter;

// Optional argument: a glTF/GLB file to load on startup.
using var app = new GltfImporterApp(args.Length > 0 ? args[0] : null);
app.Run();
