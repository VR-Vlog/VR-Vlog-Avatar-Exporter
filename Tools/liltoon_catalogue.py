"""Read lilToon shader declarations, render the 2.3.4 catalogue and hash the shader structure.

The catalogue in Editor/LilToon234Catalogue.cs is generated from the Built-in RP shaders in
`Assets/lilToon/Shader/*.shader` of a lilToon tag. Only the standard library is used so the
validation workflow can run this without installing packages.
"""
import hashlib
import io
import json
import re
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
from pathlib import Path

LILTOON_REPOSITORY = "lilxyzw/lilToon"
CATALOGUE_PATH = "Editor/LilToon234Catalogue.cs"
STRUCTURE_PATH = "Compatibility/liltoon-structure.json"
STRUCTURE_SCHEMA = 1
REFERENCE_VERSION = "2.3.4"
# The range recorded in Compatibility/dependencies.json (lilToon.supported); tests use it as a fixture.
DEFAULT_SUPPORTED_RANGE = ("2.0.0", "2.3.4")

# Shader files whose names start with these prefixes are internal pass libraries and bakers,
# not shaders a material can use. Their properties still enter the property table.
MATERIAL_SHADER_EXCLUDED_PREFIXES = ("ltspass_",)

# The exporter's own classification of declared properties. Everything else is "runtime",
# meaning the app reads it from the exported material. This is exporter policy, not lilToon data.
EDITOR_PROPERTIES = frozenset(
    ["_egci", "_egai", "_e2gci", "_e2gai", "_IDMaskCompile", "_BaseColor", "_BaseMap",
     "_BaseColorMap", "_lilToonVersion", "_Ramp", "_PackingTexture1", "_PackingTexture2",
     "_PackingTexture3", "_PackingTexture4", "_PackingChannel1", "_PackingChannel2",
     "_PackingChannel3", "_PackingChannel4", "_MainTex3D", "_ResX", "_ResY"]
    + ["_eg%s%d" % (kind, index) for kind in "ca" for index in range(8)]
    + ["_e2g%s%d" % (kind, index) for kind in "ca" for index in range(8)])
EXTERNAL_PROPERTIES = frozenset(
    ["_EnvRimBorder", "_EnvRimBlur", "_UseAudioLink", "_AudioLinkDefaultValue",
     "_AudioLinkUVMode", "_AudioLinkUVParams", "_AudioLinkStart", "_AudioLinkMask",
     "_AudioLinkMask_ScrollRotate", "_AudioLinkMask_UVMode", "_AudioLink2Main2nd",
     "_AudioLink2Main3rd", "_AudioLink2Emission", "_AudioLink2EmissionGrad",
     "_AudioLink2Emission2nd", "_AudioLink2Emission2ndGrad", "_AudioLink2Vertex",
     "_AudioLinkVertexUVMode", "_AudioLinkVertexUVParams", "_AudioLinkVertexStart",
     "_AudioLinkVertexStrength", "_AudioLinkAsLocal", "_AudioLinkLocalMap",
     "_AudioLinkLocalMapParams"])

# ShaderLab property type -> (catalogue type, texture dimension). Faces follow Unity: 1 for
# 2D, 6 for a cubemap; a volume texture is recorded as "3D" in the structure only.
PROPERTY_TYPES = {
    "float": ("float", None), "int": ("float", None), "integer": ("float", None),
    "range": ("float", None), "color": ("color", None), "vector": ("vector", None),
    "2d": ("texture", "2D"), "3d": ("texture", "3D"), "cube": ("texture", "Cube"),
    "2darray": ("texture", "2DArray"), "cubearray": ("texture", "CubeArray"),
}
TEXTURE_FACES = {"2D": 1, "Cube": 6}


class CatalogueError(ValueError):
    pass


def strip_comments(text):
    """Remove // and /* */ comments outside string literals, keeping line breaks."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"' and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            end = n if j < 0 else j + 2
            out.append("\n" * text.count("\n", i, end))
            i = end
        else:
            out.append(c)
            i += 1
    return "".join(out)


def _block(text, start):
    """Return (body, end) of the brace block whose `{` is at or after `start`."""
    open_at = text.index("{", start)
    depth = 0
    i = open_at
    in_string = False
    while i < len(text):
        c = text[i]
        if in_string:
            if c == '"':
                in_string = False
        elif c == '"':
            in_string = True
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return text[open_at + 1:i], i + 1
        i += 1
    raise CatalogueError("Unbalanced braces")


# One complete declaration: attributes (may span lines), name, display name, type and default.
_PROPERTY = re.compile(
    r'(?:\[[^\]]*\]\s*)*(?P<name>[A-Za-z_]\w*)\s*\(\s*"(?:[^"\\\n]|\\.)*"\s*,\s*(?P<type>[A-Za-z0-9]+)'
    r'\s*(?:\([^()]*\))?\s*\)\s*=\s*(?:"[^"\n]*"\s*\{[^{}]*\}|\([^()]*\)|[-+0-9.eE]+)')
_SHADER = re.compile(r'^\s*Shader\s+"(?P<name>[^"]+)"')
_KEYWORD = re.compile(r'\b(?P<word>Properties|SubShader|Pass|UsePass|GrabPass|HLSLPROGRAM|CGPROGRAM|HLSLINCLUDE|CGINCLUDE)\b')
_PROGRAM_END = {"HLSLPROGRAM": "ENDHLSL", "CGPROGRAM": "ENDCG", "HLSLINCLUDE": "ENDHLSL", "CGINCLUDE": "ENDCG"}


def _skip_program(text, word, start):
    end = re.compile(r"\b%s\b" % _PROGRAM_END[word]).search(text, start)
    if not end:
        raise CatalogueError("Missing " + _PROGRAM_END[word])
    return end.end()


def parse_shader(text):
    """Parse one Built-in RP .shader file into name, properties and passes."""
    text = strip_comments(text.lstrip("\ufeff"))
    match = _SHADER.search(text)
    if not match:
        raise CatalogueError("No Shader declaration")
    name = match.group("name")
    body, _ = _block(text, match.end())
    properties = []
    seen = set()
    passes = []
    subshaders = 0
    i = 0
    while True:
        keyword = _KEYWORD.search(body, i)
        if not keyword:
            break
        word = keyword.group("word")
        if word in _PROGRAM_END:
            i = _skip_program(body, word, keyword.end())
        elif word == "Properties":
            block, i = _block(body, keyword.end())
            # Fail instead of silently skipping a declaration the pattern cannot read.
            unread = _PROPERTY.sub("", block).strip()
            if unread:
                raise CatalogueError("Unreadable property declaration in %s: %s" % (name, unread[:80]))
            for prop in _PROPERTY.finditer(block):
                kind = prop.group("type").lower()
                if kind not in PROPERTY_TYPES:
                    raise CatalogueError("Unknown property type %s in %s" % (prop.group("type"), name))
                # Unity keeps the first declaration of a repeated name.
                if prop.group("name") in seen:
                    continue
                seen.add(prop.group("name"))
                properties.append((prop.group("name"),) + PROPERTY_TYPES[kind])
        elif word == "SubShader":
            # Built-in RP lilToon shaders have one SubShader; a second would mix fallback passes in.
            subshaders += 1
            if subshaders > 1:
                raise CatalogueError("More than one SubShader in " + name)
            i = keyword.end()
        elif word == "UsePass":
            ref = re.compile(r'\s*"([^"]+)"').match(body, keyword.end())
            if not ref or "/" not in ref.group(1):
                raise CatalogueError("Malformed UsePass in " + name)
            shader_name, pass_name = ref.group(1).rsplit("/", 1)
            passes.append({"use": (shader_name, pass_name.upper())})
            i = ref.end()
        elif word == "GrabPass":
            _, i = _block(body, keyword.end())
            passes.append({"name": "", "lightMode": "", "grab": True})
        else:  # Pass
            following = body[keyword.end():].lstrip()
            if not following.startswith("{"):
                i = keyword.end()
                continue
            block, i = _block(body, keyword.end())
            passes.append(_parse_pass(block))
    return {"name": name, "properties": properties, "passes": passes}


def _parse_pass(block):
    # Program blocks can contain Name/Tags-looking text; only read the state before them.
    head = re.split(r"\b(?:HLSLPROGRAM|CGPROGRAM)\b", block, maxsplit=1)[0]
    name = re.search(r'\bName\s+"([^"]*)"', head)
    mode = re.search(r'"LightMode"\s*=\s*"([^"]*)"', head)
    return {"name": name.group(1).upper() if name else "", "lightMode": mode.group(1) if mode else ""}


def find_shader_dir(source):
    source = Path(source)
    for candidate in (source / "Assets/lilToon/Shader", source / "Shader", source):
        if (candidate / "lts.shader").is_file():
            return candidate
    raise CatalogueError("lilToon Shader folder with lts.shader was not found under " + str(source))


def read_package_version(shader_dir):
    package = shader_dir.parent / "package.json"
    if package.is_file():
        return json.loads(package.read_text(encoding="utf-8-sig")).get("version")
    return None


def load_source(source):
    """Load every .shader in the lilToon Shader folder, sorted by file name (ordinal)."""
    shader_dir = find_shader_dir(source)
    files = sorted((p for p in shader_dir.glob("*.shader")), key=lambda p: p.name)
    shaders = []
    for path in files:
        parsed = parse_shader(path.read_text(encoding="utf-8-sig"))
        parsed["file"] = path.stem
        shaders.append(parsed)
    _resolve_use_pass(shaders)
    return {"version": read_package_version(shader_dir), "shaders": shaders}


def _resolve_use_pass(shaders):
    by_name = {s["name"]: s for s in shaders}
    for shader in shaders:
        resolved = []
        for item in shader["passes"]:
            if "use" not in item:
                resolved.append(item)
                continue
            target_name, pass_name = item["use"]
            target = by_name.get(target_name)
            matches = [p for p in (target or {}).get("passes", []) if p.get("name") == pass_name]
            if not matches:
                raise CatalogueError("UsePass %s/%s in %s does not resolve" % (target_name, pass_name, shader["name"]))
            resolved.append(dict(matches[0]))
        shader["passes"] = resolved


def property_role(name):
    if name in EDITOR_PROPERTIES:
        return "editor"
    if name in EXTERNAL_PROPERTIES:
        return "external"
    return "runtime"


def material_shaders(source):
    return [s for s in source["shaders"] if not s["file"].startswith(MATERIAL_SHADER_EXCLUDED_PREFIXES)]


def build_catalogue(source, commit):
    """Build the catalogue model; property declarations must agree across shaders."""
    properties = {}
    order = []
    for shader in source["shaders"]:
        for name, kind, dimension in shader["properties"]:
            if name == "_DummyProperty":
                continue
            previous = properties.get(name)
            if previous is None:
                properties[name] = (kind, dimension)
                order.append(name)
            elif previous != (kind, dimension):
                raise CatalogueError("Property %s is declared as %s and %s" % (name, previous, (kind, dimension)))
    shaders = []
    for shader in material_shaders(source):
        named = [p for p in shader["passes"] if p.get("name")]
        # Unnamed passes are left out of the catalogue. lilToon only has GrabPass and a fake
        # pass with LightMode "Never" next to UsePass; any other unnamed pass must be reviewed.
        for unnamed in (p for p in shader["passes"] if not p.get("name") and not p.get("grab")):
            if unnamed.get("lightMode") != "Never":
                raise CatalogueError("Unnamed pass with LightMode %r in %s" % (unnamed.get("lightMode"), shader["name"]))
        shaders.append({
            "name": shader["name"], "file": shader["file"],
            "required": [n for n, _, _ in shader["properties"] if n != "_DummyProperty" and property_role(n) == "runtime"],
            "passes": [(p["name"], p["lightMode"]) for p in named],
        })
    return {
        "version": source.get("version"), "commit": commit,
        "properties": [(n, properties[n][0], properties[n][1], property_role(n)) for n in order],
        "shaders": shaders,
    }


def render_catalogue(catalogue):
    """Render Editor/LilToon234Catalogue.cs exactly as committed."""
    version = catalogue["version"]
    o = []
    w = o.append
    w("// Generated from the official lilToon %s shader declarations." % version)
    w("using System;")
    w("using System.Collections.Generic;")
    w("namespace VRVlog.LilToon")
    w("{")
    w("    internal static class LilToon%sCatalogue" % version.replace(".", ""))
    w("    {")
    w('        internal const string Commit = "%s";' % catalogue["commit"])
    w('        internal const string Version = "%s";' % version)
    head = "        internal static readonly Dictionary<string, %s> %s = new Dictionary<string, %s>(StringComparer.Ordinal)"

    def section(value_type, title, rows):
        w(head % (value_type, title, value_type))
        w("        {")
        o.extend("            " + row for row in rows)
        w("        };")

    section("string", "Shaders", ['{"%s", "%s"},' % (s["name"], s["file"]) for s in catalogue["shaders"]])
    section("string", "Properties", ['{"%s", "%s:%s"},' % (n, k, r) for n, k, _, r in catalogue["properties"]])
    split = '.Split(new[]{\',\'}, StringSplitOptions.RemoveEmptyEntries)},'
    section("string[]", "RequiredProperties",
            ['{"%s", "%s"%s' % (s["name"], ",".join(s["required"]), split) for s in catalogue["shaders"]])
    section("string[]", "Passes",
            ['{"%s", "%s"%s' % (s["name"], ",".join(sorted({p for p, _ in s["passes"]})), split)
             for s in catalogue["shaders"]])
    faces = []
    for name, kind, dimension, role in catalogue["properties"]:
        if kind == "texture" and role == "runtime":
            if dimension not in TEXTURE_FACES:
                raise CatalogueError("Runtime texture %s has unsupported dimension %s" % (name, dimension))
            faces.append('{"%s", %d},' % (name, TEXTURE_FACES[dimension]))
    section("int", "TextureFaces", faces)
    inner = "new Dictionary<string, string>(StringComparer.Ordinal)"
    section("Dictionary<string, string>", "PassLightModes",
            ['{"%s", %s {%s}},' % (s["name"], inner, ", ".join('{"%s", "%s"}' % p for p in _unique(s["passes"])))
             for s in catalogue["shaders"]])
    w("    }")
    w("}")
    return "\n".join(o) + "\n"


def _unique(pairs):
    seen = set()
    for name, mode in pairs:
        if name not in seen:
            seen.add(name)
            yield name, mode


_CS_HEADER = re.compile(r'^// Generated from the official lilToon (?P<version>\S+) shader declarations\.$', re.MULTILINE)
_CS_SECTION = re.compile(
    r'internal static readonly Dictionary<string, [^=]+> (?P<title>\w+) = new [^\n]+\n\s*\{\n(?P<rows>.*?)\n\s*\};',
    re.DOTALL)
_CS_PAIR = re.compile(r'\{"([^"]*)", "([^"]*)"\}')


def parse_catalogue(text):
    """Read the committed C# catalogue back into the model used by build_catalogue."""
    header = _CS_HEADER.search(text)
    commit = re.search(r'internal const string Commit = "([0-9a-f]{40})";', text)
    version = re.search(r'internal const string Version = "([^"]+)";', text)
    if not header or not commit or not version or header.group("version") != version.group(1):
        raise CatalogueError("Catalogue header, Commit or Version is missing or inconsistent")
    sections = {m.group("title"): m.group("rows").split("\n") for m in _CS_SECTION.finditer(text)}
    expected = ["Shaders", "Properties", "RequiredProperties", "Passes", "TextureFaces", "PassLightModes"]
    if list(sections) != expected:
        raise CatalogueError("Catalogue sections are %s, expected %s" % (list(sections), expected))

    def rows(title, pattern):
        result = []
        for line in sections[title]:
            match = re.fullmatch(r"\s*" + pattern, line)
            if not match:
                raise CatalogueError("Unreadable %s row: %s" % (title, line.strip()[:120]))
            result.append(match.groups())
        return result

    def unique(title, pairs):
        names = [p[0] for p in pairs]
        if len(set(names)) != len(names):
            raise CatalogueError(title + " lists a key twice")
        return {p[0]: p[1] for p in pairs}

    split = r"\.Split\(new\[\]\{','\}, StringSplitOptions\.RemoveEmptyEntries\)\},"
    shaders = rows("Shaders", r'\{"([^"]+)", "([^"]+)"\},')
    properties = rows("Properties", r'\{"([^"]+)", "(float|color|vector|texture):(runtime|editor|external)"\},')
    unique("Shaders", shaders)
    unique("Properties", properties)
    required = unique("RequiredProperties", rows("RequiredProperties", r'\{"([^"]+)", "([^"]*)"' + split))
    pass_sets = unique("Passes", rows("Passes", r'\{"([^"]+)", "([^"]*)"' + split))
    faces = unique("TextureFaces", rows("TextureFaces", r'\{"([^"]+)", (\d+)\},'))
    modes = unique("PassLightModes", rows(
        "PassLightModes", r'\{"([^"]+)", new Dictionary<string, string>\(StringComparer\.Ordinal\) \{(.*)\}\},'))
    dimension_of_faces = {v: k for k, v in TEXTURE_FACES.items()}
    model_properties = []
    for name, kind, role in properties:
        dimension = None
        if name in faces:
            if kind != "texture" or role != "runtime" or int(faces[name]) not in dimension_of_faces:
                raise CatalogueError("TextureFaces entry %s does not belong to a runtime texture" % name)
            dimension = dimension_of_faces[int(faces[name])]
        elif kind == "texture" and role == "runtime":
            raise CatalogueError("Runtime texture %s has no TextureFaces entry" % name)
        model_properties.append((name, kind, dimension, role))
    if set(faces) - {p[0] for p in properties}:
        raise CatalogueError("TextureFaces lists an undeclared property")
    model_shaders = []
    for name, file in shaders:
        if name not in required or name not in pass_sets or name not in modes:
            raise CatalogueError("Shader %s is missing from a per-shader section" % name)
        passes = _CS_PAIR.findall(modes[name])
        if sorted(p for p, _ in passes) != sorted(x for x in pass_sets[name].split(",") if x):
            raise CatalogueError("Passes and PassLightModes disagree for " + name)
        model_shaders.append({"name": name, "file": file,
                              "required": [x for x in required[name].split(",") if x], "passes": passes})
    for title, table in (("RequiredProperties", required), ("Passes", pass_sets), ("PassLightModes", modes)):
        if len(table) != len(shaders):
            raise CatalogueError(title + " lists a shader that is not in Shaders")
    return {"version": version.group(1), "commit": commit.group(1),
            "properties": model_properties, "shaders": model_shaders}


def structure(catalogue):
    """Canonical structure: what the exporter checks, without order, defaults or display text.

    Included: material shader names and file names; each declared property's name, type,
    exporter role and (for runtime textures) dimension; each shader's runtime properties;
    each shader's named passes and their LightMode. Excluded: version, commit, declaration
    order, default values, display names, attributes, unnamed passes and GrabPass.
    """
    roles = {}
    for name, kind, dimension, role in catalogue["properties"]:
        roles[name] = (kind, role, dimension if kind == "texture" and role == "runtime" else None)
    shaders = []
    for shader in catalogue["shaders"]:
        unknown = [n for n in shader["required"] if n not in roles]
        if unknown:
            raise CatalogueError("Shader %s uses undeclared properties %s" % (shader["name"], unknown))
        shaders.append([shader["name"], shader["file"], sorted(set(shader["required"])),
                        sorted([p, m] for p, m in set(map(tuple, shader["passes"])))])
    return {
        "schema": STRUCTURE_SCHEMA,
        "properties": sorted([n, k, r, d or ""] for n, (k, r, d) in roles.items()),
        "shaders": sorted(shaders),
    }


def structure_hash(catalogue):
    canonical = json.dumps(structure(catalogue), ensure_ascii=True, separators=(",", ":"), sort_keys=True)
    return hashlib.sha256(canonical.encode("ascii")).hexdigest()


def structure_difference(left, right):
    """Short human-readable differences between two catalogue models (left -> right)."""
    a, b = structure(left), structure(right)
    props_a = {p[0]: p[1:] for p in a["properties"]}
    props_b = {p[0]: p[1:] for p in b["properties"]}
    shaders_a = {s[0]: s[1:] for s in a["shaders"]}
    shaders_b = {s[0]: s[1:] for s in b["shaders"]}
    lines = []
    lines += ["property removed: " + n for n in sorted(set(props_a) - set(props_b))]
    lines += ["property added: " + n for n in sorted(set(props_b) - set(props_a))]
    lines += ["property %s: %s -> %s" % (n, props_a[n], props_b[n])
              for n in sorted(set(props_a) & set(props_b)) if props_a[n] != props_b[n]]
    lines += ["shader removed: " + n for n in sorted(set(shaders_a) - set(shaders_b))]
    lines += ["shader added: " + n for n in sorted(set(shaders_b) - set(shaders_a))]
    for name in sorted(set(shaders_a) & set(shaders_b)):
        (file_a, req_a, pass_a), (file_b, req_b, pass_b) = shaders_a[name], shaders_b[name]
        if file_a != file_b:
            lines.append("shader %s file: %s -> %s" % (name, file_a, file_b))
        for prop in sorted(set(req_a) - set(req_b)):
            lines.append("shader %s: property removed: %s" % (name, prop))
        for prop in sorted(set(req_b) - set(req_a)):
            lines.append("shader %s: property added: %s" % (name, prop))
        if pass_a != pass_b:
            lines.append("shader %s passes: %s -> %s" % (name, pass_a, pass_b))
    return lines


def parse_version(value):
    match = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", str(value))
    if not match:
        raise CatalogueError("Not a stable version tag: %s" % (value,))
    return tuple(int(x) for x in match.groups())


def supported_range(config):
    """Supported lilToon range from dependencies.json (lilToon.supported)."""
    supported = config["lilToon"]["supported"]
    return supported["minimum"], supported["maximum"]


def in_range(tag, bounds):
    return parse_version(bounds[0]) <= parse_version(tag) <= parse_version(bounds[1])


def git_tag_source(repository, tag, destination):
    """Extract a tag from a local lilToon clone without touching its working tree."""
    commit = subprocess.check_output(
        ["git", "-C", str(repository), "rev-parse", "--verify", "refs/tags/" + tag + "^{commit}"], text=True).strip()
    archive = subprocess.check_output(["git", "-C", str(repository), "archive", "--format=tar", commit])
    _extract_tar(io.BytesIO(archive), Path(destination))
    return commit


MAX_ARCHIVE_BYTES = 256 * 1024 * 1024
MAX_EXTRACTED_BYTES = 1024 * 1024 * 1024


def fetch_tag_source(tag, destination, repository=LILTOON_REPOSITORY):
    """Download a GitHub tag archive into `destination` and return its commit."""
    if not re.fullmatch(r"[0-9A-Za-z._-]+", tag):
        raise CatalogueError("Invalid tag: " + tag)
    url = "https://codeload.github.com/%s/tar.gz/refs/tags/%s" % (repository, tag)
    request = urllib.request.Request(url, headers={"User-Agent": "VRVlog-compatibility"})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read(MAX_ARCHIVE_BYTES + 1)
    if len(data) > MAX_ARCHIVE_BYTES:
        raise CatalogueError("Archive exceeds limit")
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
        commit = archive.pax_headers.get("comment", "")
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise CatalogueError("The archive does not record its commit")
    _extract_tar(io.BytesIO(data), Path(destination), strip_top=True)
    return commit


def remote_tags(repository=LILTOON_REPOSITORY, pages=10):
    """Tag names of the lilToon repository on GitHub."""
    names = []
    for page in range(1, pages + 1):
        request = urllib.request.Request(
            "https://api.github.com/repos/%s/tags?per_page=100&page=%d" % (repository, page),
            headers={"Accept": "application/vnd.github+json", "User-Agent": "VRVlog-compatibility"})
        with urllib.request.urlopen(request, timeout=20) as response:
            data = response.read(2 * 1024 * 1024 + 1)
        if len(data) > 2 * 1024 * 1024:
            raise CatalogueError("Tag response exceeds limit")
        batch = [item["name"] for item in json.loads(data)]
        names += batch
        if len(batch) < 100:
            return names
    raise CatalogueError("Too many tags")


def _extract_tar(stream, destination, strip_top=False):
    destination.mkdir(parents=True, exist_ok=True)
    root = destination.resolve()
    extracted = 0
    with tarfile.open(fileobj=stream, mode="r:*") as archive:
        for member in archive:
            extracted += max(member.size, 0)
            if extracted > MAX_EXTRACTED_BYTES:
                raise CatalogueError("Archive contents exceed limit")
            parts = Path(member.name).parts
            if strip_top:
                parts = parts[1:]
            # Links and devices are skipped; only plain files and folders are needed.
            if not parts or not (member.isfile() or member.isdir()):
                continue
            target = (root / Path(*parts)).resolve()
            if root not in target.parents:
                raise CatalogueError("Archive member escapes destination: " + member.name)
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, open(target, "wb") as out:
                shutil.copyfileobj(source, out)


def catalogue_for_tag(tag, git_repository=None):
    """Load a tag from a local clone or GitHub into a temporary folder and build its model."""
    with tempfile.TemporaryDirectory(prefix="liltoon-" + tag + "-") as temp:
        if git_repository:
            commit = git_tag_source(git_repository, tag, temp)
        else:
            commit = fetch_tag_source(tag, temp)
        return build_catalogue(load_source(temp), commit)


TABLE_KEYS = {"format", "definition", "reference", "tags"}
REFERENCE_KEYS = {"tag", "commit", "structureHash"}
TAG_KEYS = {"tag", "commit", "structureHash", "matchesReference"}


def check_structure_table(table, catalogue, bounds):
    """Return problems in Compatibility/liltoon-structure.json; an empty list means it passes."""
    problems = []
    if (not isinstance(table, dict) or set(table) != TABLE_KEYS or table.get("format") != 1
            or not isinstance(table.get("tags"), list)):
        return ["structure table must have exactly %s with format 1 and a tags list" % sorted(TABLE_KEYS)]
    reference = table.get("reference")
    if (not isinstance(reference, dict) or set(reference) != REFERENCE_KEYS
            or reference.get("tag") != catalogue["version"]):
        return ["reference must have %s for the catalogue version %s" % (sorted(REFERENCE_KEYS), catalogue["version"])]
    tags = {}
    order = []
    for entry in table["tags"]:
        if not isinstance(entry, dict) or set(entry) != TAG_KEYS:
            problems.append("malformed tag entry: %r" % (entry,))
            continue
        try:
            parse_version(entry["tag"])
        except CatalogueError as error:
            problems.append(str(error))
            continue
        if (not re.fullmatch(r"[0-9a-f]{40}", str(entry["commit"]))
                or not re.fullmatch(r"[0-9a-f]{64}", str(entry["structureHash"]))
                or not isinstance(entry["matchesReference"], bool)):
            problems.append("tag %s has a malformed commit, hash or flag" % entry["tag"])
        if entry["tag"] in tags:
            problems.append("tag %s is listed twice" % entry["tag"])
        tags[entry["tag"]] = entry
        order.append(entry["tag"])
    if order != sorted(order, key=parse_version):
        problems.append("tags must be sorted by version")
    expected = structure_hash(catalogue)
    if reference.get("commit") != catalogue["commit"]:
        problems.append("reference commit differs from the catalogue commit")
    if reference.get("structureHash") != expected:
        problems.append("reference hash %s differs from the hash of %s (%s)"
                        % (reference.get("structureHash"), CATALOGUE_PATH, expected))
    own = tags.get(catalogue["version"])
    if own is None:
        problems.append("the catalogue version %s is not in tags" % catalogue["version"])
    elif own["commit"] != catalogue["commit"] or own["structureHash"] != expected:
        problems.append("tag %s does not match the catalogue" % catalogue["version"])
    for entry in tags.values():
        if entry["matchesReference"] != (entry["structureHash"] == expected):
            problems.append("tag %s has a wrong matchesReference flag" % entry["tag"])
    for bound in bounds:
        if bound not in tags:
            problems.append("supported range bound %s is not in tags" % bound)
    for entry in tags.values():
        if in_range(entry["tag"], bounds) and entry["structureHash"] != expected:
            problems.append("tag %s is in the supported range %s-%s but its structure differs from %s"
                            % (entry["tag"], bounds[0], bounds[1], catalogue["version"]))
    return problems
