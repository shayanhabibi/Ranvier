import pathlib
import subprocess
import tempfile
import json
import shutil
import re

root = pathlib.Path(__file__).resolve().parent
with tempfile.TemporaryDirectory() as folder:
    first = pathlib.Path(folder) / "first.fs"
    second = pathlib.Path(folder) / "second.fs"
    for target in (first, second):
        result = subprocess.run(["rtk", "dotnet", "run", "--project", str(root / "Ranvier.Dom.Generate.fsproj"), "-c", "Release", "--", str(target)], capture_output=True, text=True)
        assert result.returncode == 0, result.stdout + result.stderr
    assert first.read_bytes() == second.read_bytes(), "regeneration must be byte-identical"
    text = first.read_text()
    assert "module Authoring =" in text, "generate overloaded authoring helpers"
    assert "static member inline" in text
    assert "module Program =" in text, "generate the bound Program element authoring surface"
    program = text.split("module Program =", 1)[1]
    assert program.count("let inline ") == 112, "every HTML tag needs one inline Program constructor"
    assert program.count("Typed.programElement ") == 112, "bound constructors must inline the typed Program path"
    assert "Typed.Modifier<Dom.HTMLDivElement> array" in text
    assert "Representation.Program.View array" in program
    assert "Typed.Modifier<Dom.HTMLDivElement> list" not in text
    assert "Representation.Program.View list" not in program
    assert "Typed.programElement (HtmlTag \"br\") modifiers [||]" in program
    assert "Dom.HTMLInputElement" in text
    assert "Dom.InputEvent -> unit" in text
    assert "Dom.FileList option" in text
    assert "let ``files`` (" in text
    assert "let filesStatic" not in text
    assert "let formStatic" not in text
    coverage = json.loads(first.with_suffix(".coverage.json").read_text())
    assert len(coverage["tags"]) > 100, "full HTML tag map must be generated"
    assert any(tag["tag"] == "br" and tag["isVoid"] for tag in coverage["tags"])
    assert all("let ``" + tag + "`` " in text for tag in ["div", "button", "input", "select", "option", "textarea", "img", "br", "article"])
    assert "let onClick " in text and "Dom.MouseEvent -> unit" in text
    assert first.with_suffix(".coverage.json").read_bytes() == second.with_suffix(".coverage.json").read_bytes()
    assert any("unsupported type" in item["reason"] for item in coverage["omissions"])
    assert coverage["attributeCount"] == 34
    assert coverage["authoringHelperCount"] == 14
    assert coverage["authoringPolicySha256"]
    assert coverage["attributePolicySha256"]
    assert "module Attrs =" in text and "let classReactive" in text
    assert "Typed.reactiveBooleanAttribute \"disabled\"" in text
    assert "Typed.reactiveAriaBooleanAttribute \"aria-expanded\"" in text
    assert "let innerHTMLStatic" not in text and "let outerHTMLStatic" not in text
    tampered = pathlib.Path(folder) / "tampered"
    shutil.copytree(root / "bin/Release/net10.0/xantham", tampered / "xantham")
    payload = tampered / "xantham/declarations.json.br"
    data = bytearray(payload.read_bytes())
    data[-1] ^= 1
    payload.write_bytes(data)
    result = subprocess.run(["rtk", "dotnet", "run", "--project", str(root / "Ranvier.Dom.Generate.fsproj"), "-c", "Release", "--no-build", "--", str(first), str(tampered)], capture_output=True, text=True)
    assert result.returncode != 0 and "Catalogue hash mismatch" in result.stderr, result.stdout + result.stderr
    assert first.read_bytes() == second.read_bytes(), "rejection must preserve existing output"
    policy_path = root / "attributes.json"
    original_policy = policy_path.read_bytes()
    try:
        for invalid_name in ("", "1title", "-title", "title\u0661"):
            policy = json.loads(original_policy)
            policy["groups"][0]["entries"][0]["name"] = invalid_name
            policy_path.write_text(json.dumps(policy), encoding="utf-8")
            result = subprocess.run(["rtk", "dotnet", "run", "--project", str(root / "Ranvier.Dom.Generate.fsproj"), "-c", "Release", "--no-build", "--", str(first)], capture_output=True, text=True)
            assert result.returncode != 0 and "Invalid attribute policy name" in result.stderr, result.stdout + result.stderr
            assert first.read_bytes() == second.read_bytes(), "invalid policy must preserve existing output"
    finally:
        policy_path.write_bytes(original_policy)
    authoring_path = root / "authoring.json"
    original_authoring = authoring_path.read_bytes()
    try:
        for name in ("files", "form", "missingMember"):
            policy = json.loads(original_authoring)
            policy["properties"][1]["members"] = [name]
            authoring_path.write_text(json.dumps(policy), encoding="utf-8")
            result = subprocess.run(["rtk", "dotnet", "run", "--project", str(root / "Ranvier.Dom.Generate.fsproj"), "-c", "Release", "--no-build", "--", str(first)], capture_output=True, text=True)
            assert result.returncode != 0, "unsafe, readonly and missing properties must be rejected"
            assert first.read_bytes() == second.read_bytes(), "invalid authoring policy must preserve existing output"
    finally:
        authoring_path.write_bytes(original_authoring)
    template = first.with_name(first.stem + "Template.fs")
    template_second = second.with_name(second.stem + "Template.fs")
    assert template.read_bytes() == template_second.read_bytes(), "template catalogue must be byte-identical"
    ttext = template.read_text()
    assert ttext.startswith("// <auto-generated>")
    assert "[<AutoOpen>]\nmodule Ranvier.View.Foundation.HtmlTemplate\n" in ttext
    assert coverage["templateTagCount"] == 112
    entries = [entry for group in json.loads(policy_path.read_bytes())["groups"] for entry in group["entries"]]
    assert coverage["templateAttributeCount"] == len(entries)
    overloads = {"string": 6, "boolean": 3, "ariaBoolean": 6}
    data_aria = 2 * 3
    attribute_parts = len(re.findall(r"TemplateParts\.(?:attribute|optionalAttribute|readAttribute|sourceAttribute)\b", ttext))
    assert attribute_parts == sum(overloads[entry["kind"]] for entry in entries) + data_aria, "string and ARIA attributes need six overloads, booleans three"
    child_replacing = {
        ("HTMLAnchorElement", "text"),
        ("HTMLOptionElement", "text"),
        ("HTMLTitleElement", "text"),
        ("HTMLScriptElement", "text"),
        ("HTMLOutputElement", "value"),
        ("HTMLOutputElement", "defaultValue"),
        ("HTMLTextAreaElement", "defaultValue"),
    }
    template_omissions = {(item["owner"], item["memberName"]) for item in coverage["templateOmissions"] if item["reason"] == "setter replaces child nodes"}
    assert template_omissions == child_replacing, "child-replacing setters are template coverage omissions"
    for owner, name in child_replacing:
        assert "member _.``" + name + "``<'native when 'native :> Dom." + owner + ">" not in ttext, "template omits child-replacing setter " + owner + "." + name
        assert "(node :> Dom." + owner + ").``" + name + "`` <- value" in text, "Html.fs keeps setter " + owner + "." + name
    assert "type Groups.TitleProperties<'inst> with" not in ttext
    assert "member _.``value``<'native when 'native :> Dom.HTMLTextAreaElement>(value: string)" in ttext, "value stays on owners whose setter keeps child nodes"
    assert coverage["templatePropertyCount"] == text.count("Typed.staticProperty (fun (node: 'native)") - len(child_replacing), "every shared settable property, except child-replacing setters, needs a template part"
    for function in ("property", "readProperty", "sourceProperty"):
        assert len(re.findall(r"TemplateParts\." + function + r"\b", ttext)) == coverage["templatePropertyCount"], "every property needs value, reader and source overloads"
    assert coverage["templateEventCount"] == len(re.findall(r"TemplateParts\.event\b", ttext))
    assert "type Groups.TextAreaAttributes<'inst> with" in ttext and "member _.TextArea: Groups.TextAreaAttributes<'inst>" in ttext
    assert "Textarea" not in ttext, "template attribute groups take the native type's group name"
    assert "    module Textarea =" in text, "Html.Attrs keeps the policy module names"
    assert "member _.``div`` (parts: Part<Dom.HTMLDivElement, 'inst> array) (children: TemplateView<'inst> array)" in ttext
    assert "member _.``br``(parts: Part<Dom.HTMLBRElement, 'inst> array) : TemplateView<'inst>" in ttext
    assert "type Groups.InputProperties<'inst> with" in ttext
    assert "member _.onClick<'native when 'native :> Dom.HTMLElement>(handler: 'inst -> Dom.PointerEvent -> unit)" in ttext
    assert "TemplateParts.readAttribute \"aria-expanded\" (fun instance -> Some (TemplateParts.ariaText (read instance)))" in ttext
    handwritten = (root / "../../experiments/Ranvier.View.Foundation/Template.fs").read_text(encoding="utf-8")
    builder_members = {}
    for builder in ("TemplateBuilder", "AttributeBuilder", "PropertyBuilder", "EventBuilder"):
        body = re.search(r"^type " + builder + r"<'inst>.*?(?=^\S)", handwritten, re.M | re.S)
        assert body, builder + " must be declared in Template.fs"
        builder_members[builder] = set(re.findall(r"^    member (?:_|this)\.(\w+)", body.group(0), re.M))
    generator = (root / "Program.fs").read_text(encoding="utf-8")
    reserved_block = generator.split("    let reserved =", 1)[1].split("    let authoringRenames", 1)[0]
    reserved = {builder: set(re.findall(r'"(\w+)"', names)) for builder, names in re.findall(r'"(\w+Builder)",\s*set\s*\[(.*?)\]', reserved_block, re.S)}
    exempt = {"AttributeBuilder": {"data", "aria"}}
    assert set(reserved) == set(builder_members), reserved
    for builder, members in builder_members.items():
        assert members, builder + " must declare handwritten members"
        assert reserved[builder] - exempt.get(builder, set()) == members, builder + ": reserved names must equal handwritten members " + repr(sorted(members ^ (reserved[builder] - exempt.get(builder, set()))))
    policy_root = pathlib.Path(folder) / "policy"
    policy_root.mkdir()
    shutil.copy(root / "authoring.json", policy_root / "authoring.json")
    collisions = [
        ("reserved flat member", lambda groups: groups[0]["entries"].append({"helper": "attribute", "name": "data", "kind": "string", "member": "id"}), "Template member takes a reserved name: AttributeBuilder.attribute"),
        ("reserved prefix member", lambda groups: groups[0]["entries"].append({"helper": "data", "name": "data", "kind": "string", "member": "id"}), "Template member takes a reserved name: AttributeBuilder.data"),
        ("merged group member", lambda groups: groups.append({"module": "TextAreaExtra", "native": "HTMLTextAreaElement", "entries": [{"helper": "disabled", "name": "disabled", "kind": "boolean", "member": "disabled"}]}), "Duplicate template member: Groups.TextAreaAttributes.disabled"),
    ]
    for name, mutate, message in collisions:
        policy = json.loads(policy_path.read_bytes())
        mutate(policy["groups"])
        (policy_root / "attributes.json").write_text(json.dumps(policy), encoding="utf-8")
        result = subprocess.run(["rtk", "dotnet", "run", "--project", str(root / "Ranvier.Dom.Generate.fsproj"), "-c", "Release", "--no-build", "--", str(first), str(root / "bin/Release/net10.0"), str(policy_root)], capture_output=True, text=True)
        assert result.returncode != 0 and message in result.stderr, name + ": " + result.stdout + result.stderr
        assert first.read_bytes() == second.read_bytes() and template.read_bytes() == template_second.read_bytes(), name + " must preserve existing output"
print("PASS: deterministic full HTML output and coverage, typed native members/events, policy exclusions, corrupt catalogue rejection")
