"""Compile valid generated usage and reject incompatible/readonly examples."""
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
project = root / "examples/Fable.Ranvier.Playground/tests/DomTests.fsproj"
prefix = """module GeneratedTypeCheck
open Fable.Core.TS
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
module H = Ranvier.View.Foundation.Generated.Html
open Ranvier.View.Foundation.Generated.Html.Authoring
open Ranvier.View.Foundation.Template
"""
examples = [
    ("valid helpers", "let view = H.div Tree.ops [| H.Properties.HTMLElement.titleStatic \"shared\"; H.Events.onClick (fun (_: Dom.PointerEvent) -> ()) |] [| H.input Tree.ops [| H.Input.checkedStatic true |]; H.select Tree.ops [||] [| H.option Tree.ops [||] [||] |]; H.br Tree.ops [||] |]", None),
    ("generic modifiers reject lists", "let view = H.div Tree.ops [] [||]", "FS0001"),
    ("generic children reject lists", "let view = H.div Tree.ops [||] []", "FS0001"),
    ("bound modifiers reject lists", "let view = H.Program.div [] [||]", "FS0001"),
    ("bound children reject lists", "let view = H.Program.div [||] []", "FS0001"),
    ("input modifier on div", "let view = H.div Tree.ops [| H.Input.checkedStatic true |] [||]", "FS0001"),
    ("wrong property value", 'let modifier = H.Input.checkedStatic "true"', "FS0001"),
    ("readonly native property", "let write (node: Dom.HTMLInputElement) = node.form <- None", "FS0810"),
    ("files setter excluded by policy", "let modifier = H.Input.filesStatic None", "FS0039"),
    ("void element rejects children", "let view = H.br Tree.ops [||] [| Tree.ops.Text \"child\" |]", "FS0001"),
    ("wrong expanded property value", "let modifier = H.Textarea.rowsStatic \"3\"", "FS0001"),
    ("DOM replacement setter excluded", "let modifier = H.Div.innerHTMLStatic \"markup\"", "FS0039"),
    ("shared input property rejects div", "let view = H.div Tree.ops [| H.Properties.HTMLInputElement.checkedStatic true |] [||]", "FS0001"),
    ("wrong mapped event type", "let modifier = H.Events.onClick (fun (_: Dom.KeyboardEvent) -> ())", "FS0001"),
    ("attribute helper rejects incompatible element", "let view = H.div Tree.ops [| H.Attrs.Input.checkedStatic true |] [||]", "FS0001"),
    ("boolean attribute rejects string value", "let modifier = H.Attrs.Button.disabledStatic \"false\"", "FS0001"),
    ("reactive attribute requires removal option", "let modifier = H.Attrs.classReactive (fun () -> \"panel\")", "FS0001"),
    ("bound Program helpers", "let view = H.Program.article [| H.Attrs.classStatic \"panel\"; H.Events.onClick ignore |] [| H.Program.input [| H.Input.checkedStatic true |]; H.Program.br [||] |]", None),
    ("bound input modifier rejects div", "let view = H.Program.div [| H.Input.checkedStatic true |] [||]", "FS0001"),
    ("bound void element rejects children", "let view = H.Program.br [||] [| Program.ops.Text \"child\" |]", "FS0003"),
    ("bound Program rejects Tree children", "let view = H.Program.div [||] [| Tree.ops.Text \"child\" |]", "FS0001"),
    ("overload source shapes", "let view (signal: global.Ranvier.Signal<string>) (memo: global.Ranvier.Memo<string>) = H.Program.input [| Props.Input.value \"fixed\"; Props.Input.value (fun () -> \"read\"); Props.Input.value signal; Props.Input.value memo; Attrs.title signal; Attrs.Input.value (None: string option) |]", None),
    ("overloaded input property rejects div", "let view = H.Program.div [| Props.Input.value \"input\" |] [||]", "FS0193"),
    ("overloaded property rejects wrong source type", "let modifier (source: global.Ranvier.Signal<int>) = Props.Input.value source", "FS0041"),
    ("overloaded attribute rejects wrong reader result", "let modifier = Attrs.title (fun () -> 123)", "FS0041"),
    ("template catalogue keywords", '''
type R = { Kind: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [||] [| t.``base`` [||]; t.button [| t.Attrs.Button.``type`` (fun r -> r.Kind) |] [||] |])
''', None),
    ("template property on another element", '''
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [| t.Props.Input.value (fun r -> r.Value) |] [||])
''', "FS0193"),
    ("template void tag with children", '''
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.br [||] [||])
''', "FS0003"),
]

with tempfile.TemporaryDirectory(prefix="ranvier-generated-types-") as directory:
    path = Path(directory)
    check = path / "Check.fs"
    fsproj = path / "Check.fsproj"
    fsproj.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>false</GenerateDocumentationFile></PropertyGroup>
  <ItemGroup><Compile Include="Check.fs" /><ProjectReference Include="{project.as_posix()}" /><PackageReference Update="FSharp.Core" Version="10.1.401" /></ItemGroup>
</Project>
''', encoding="utf-8")
    check.write_text(prefix + examples[0][1] + "\n", encoding="utf-8")
    restored = subprocess.run(["rtk", "proxy", "dotnet", "restore", str(fsproj), "--configfile", str(root / "NuGet.Config")], capture_output=True, text=True)
    if restored.returncode:
        raise RuntimeError(restored.stdout + restored.stderr)
    for name, body, diagnostic in examples:
        check.write_text(prefix + body + "\n", encoding="utf-8")
        result = subprocess.run(["rtk", "proxy", "dotnet", "build", str(fsproj), "--no-restore", "--configuration", "Release"], capture_output=True, text=True)
        output = result.stdout + result.stderr
        if diagnostic is None:
            if result.returncode:
                raise AssertionError(f"{name} failed:\n{output}")
        elif result.returncode == 0 or f"error {diagnostic}" not in output:
            raise AssertionError(f"{name} did not fail with {diagnostic}:\n{output}")
        print(f"PASS {name}")
