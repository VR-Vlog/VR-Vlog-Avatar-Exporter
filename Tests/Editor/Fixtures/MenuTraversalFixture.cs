using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter.Tests
{
    // SDK-shaped public fields exercise the production reflection/traversal code.
    // No SDK installation, controller execution or source avatar is simulated.
    internal static class MenuTraversalFixture
    {
        internal static void Run(Action<bool, string> check)
        {
            var sub = new Menu();
            sub.controls.Add(new Control { name = "笑顔", type = "Toggle", parameter = new Parameter { name = "Face" }, value = 3 });
            sub.controls.Add(new Control { name = "強さ", type = "RadialPuppet" });
            sub.controls.Add(new Control { name = "未設定", type = "Button" });
            var menu = new Menu();
            menu.controls.Add(new Control { name = "表情", type = "SubMenu", parameter = new Parameter { name = "OpenFace" }, value = 1, subMenu = sub });
            sub.controls.Add(new Control { name = "循環", type = "SubMenu", subMenu = menu });
            var source = Read(menu);
            check(source.Entries.Count == 3, "Menus produce control entries, not individual morph names.");
            check(source.Entries[0].Name == "表情 / 笑顔" && source.Entries[0].Error == null, "Composed expression retains its menu/submenu name.");
            check(source.Entries[0].VrmName() == "笑顔", "The VRM name is the item label without its submenu path.");
            check(new VrChatExpressionMenu.Entry { Name = "表情 / 笑顔", ExportName = " " }.VrmName() == "笑顔" && new VrChatExpressionMenu.Entry { Name = "笑顔" }.VrmName() == "笑顔",
                "A blank VRM name falls back to the last element of the path.");
            check(source.Entries[0].Parameters["Face"] == 3 && source.Entries[0].Parameters["OpenFace"] == 1,
                "Child selection carries its own parameter and the ancestor submenu gate.");
            check(source.Entries[1].Error.Contains("Puppet") && source.Entries[2].Error != null, "Puppets and missing parameters have explicit per-item errors.");
            check(source.Messages.Single().Contains("循環"), "A menu cycle terminates and is reported.");
            var previousId = source.Entries[0].Id;
            sub.controls[0].value = 2;
            check(Read(menu).Entries[0].Id != previousId, "Changed menu controls cannot inherit another control's stale exclusion.");
            menu.controls.Add(new Control { name = "別経路", type = "SubMenu", subMenu = sub });
            source = Read(menu);
            check(source.Entries.Count == 6 && source.Entries[3].Name == "別経路 / 笑顔", "Shared submenu assets can be used through multiple independent paths.");
            check(source.Entries[0].VrmName() == "笑顔" && source.Entries[3].VrmName() == "笑顔", "The same label in two submenus keeps one VRM name; registration numbers the second.");
            check(!source.Entries[3].Parameters.ContainsKey("OpenFace"), "Ancestor parameters do not leak between menu branches.");
            sub.controls[0].value = float.NaN;
            bool rejected = false;
            try { Read(menu); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Non-finite SDK values are rejected before Animator evaluation.");

            var large = new Menu();
            for (var i = 0; i < 300; i++) large.controls.Add(new Control { name = "face " + i, type = "Toggle", parameter = new Parameter { name = "Face" }, value = i });
            large.controls.Add(new Control { name = "Empty", type = "SubMenu", subMenu = new Menu() });
            check(Read(large).Entries.Count == 300 && Read(large).Entries.Last().Name == "face 299", "A 300-item menu is read in full.");

            var shared = new Menu();
            shared.controls.Add(new Control { name = "笑顔", type = "Toggle", parameter = new Parameter { name = "Face" }, value = 1 });
            var twice = new Menu();
            twice.controls.Add(new Control { name = "経路A", type = "SubMenu", parameter = new Parameter { name = "OpenFace" }, value = 1, subMenu = shared });
            twice.controls.Add(new Control { name = "経路B", type = "SubMenu", parameter = new Parameter { name = "OpenFace" }, value = 1, subMenu = shared });
            source = Read(twice);
            check(source.Entries.Count == 1 && source.Entries[0].Name == "経路A / 笑顔" && source.Messages.Count == 0,
                "A shared submenu reached again with the same parent values adds its entries once.");

            var skip = new VrChatMenuImportPolicy { SkipAll = true };
            check(VrChatExpressionMenu.ReadMenu(large, skip).Entries.Count == 0,
                "An explicitly chosen whole-menu omission does not truncate or mutate the source menu.");
            check(large.controls.Count == 301, "Omission leaves the original controls and empty submenu intact.");
        }

        private static VrChatExpressionMenu.Source Read(Menu menu)
        {
            return VrChatExpressionMenu.ReadMenu(menu);
        }
        private sealed class Menu { public List<Control> controls = new List<Control>(); }
        private sealed class Parameter { public string name; }
        private sealed class Control
        {
            public string name, type;
            public Parameter parameter;
            public float value;
            public Menu subMenu;
        }
    }
}
