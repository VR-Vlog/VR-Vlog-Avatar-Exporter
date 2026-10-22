> Stable **0.11.15** adopts the published 0.11.15-beta.2 features: expression and pose file selection, previews, QR transfer to a compatible iPhone version of VR Vlog, remembered privacy consent, and the fix for NDMF notifications causing `The scene is invalid.`. Select it in ALCOM without enabling **Show pre-release packages**. Uploads start only when you press the button. Previously published ZIPs remain unchanged.

# VR Vlog lilToon VRM Exporter

[日本語](README.md) · [English](README.en.md)

[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Validate](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/actions/workflows/validate.yml/badge.svg?branch=main)](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/actions/workflows/validate.yml)

Export your lilToon avatar to **VRM 1.0** for VR Vlog on iPhone. This Unity package saves a standard MToon fallback and a validated lilToon extension in the same file, using temporary copies to preserve the source avatar.

[Install](https://vrvlog.fun/exporter/) · [Latest stable release](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/releases/latest) · [Changelog](CHANGELOG.md) · [Contributing](CONTRIBUTING.md)

## Features

- Export lilToon material settings and textures for VR Vlog's dedicated renderer, with a standard MToon fallback for other VRM viewers.
- Preserve supported expressions, blinking, tracking morphs and appearance prepared by Modular Avatar / NDMF.
- Convert supported PhysBones to VRM SpringBones, with diagnostics for approximations and unsupported settings.
- Set a bone-type VRM 1.0 look-at from the midpoint of the eyes when both eye bones exist. Its range comes from the Avatar Descriptor's Eye Look poses when they can be measured; otherwise UniVRM's default range is used. A VRM 1.0 look-at already edited in the avatar's Vrm10Instance settings is kept.
- Inspect export failures and try supported recovery options on a fresh copy of the avatar.

The exporter does not reproduce every shader effect, Animator behavior or VRChat gimmick. See the [detailed scope and limitations in Japanese](README.md#対応範囲と制限).

Materials on shaders other than lilToon are replaced with official lilToon. For toon shaders with a lighting type (`_LightingMode`) and switchable sections, each section is mapped onto the lilToon item with the same role: shadows (border and blur per lighting type, shadow colors, a second shadow), the light limits, color adjustment (hue, saturation, brightness, gamma, gradation), two matcaps, rim light, outline, emission (including blinking), alpha mask and stencil. Sections official lilToon does not have (decals, dissolve, glitter, AudioLink and others) are not exported; the material and section names are listed before saving. Hue (when the source rotates it in OKLab), saturation and ramp-texture shadows are close approximations, not exact.

With **補助ギミックを自動除外** (auto-exclude helper gimmicks) on, which is the default, the export leaves out what does nothing in a VRM:

- VRChat-only gimmicks: an object with a Modular Avatar menu or animator merge that contains a camera, sound, particles, a light, a contact or a constraint, and whose meshes are not drawn like the avatar (lilToon, MToon or a shader shared with the body). It is removed as a whole. Outfits bound to the body's bones and props drawn like the avatar stay. Gimmicks without VRChat-only parts are only listed for review.
- Objects that a Modular Avatar Platform Filter leaves out of the generic (VRM) platform. Objects whose filters both include and exclude platforms are only listed for review.
- Empty objects with no mesh and no bones, such as holders of VRChat menus or settings. The number left out is logged to the Console.

Removed and reviewed objects appear in the export result list. To keep one, choose **含める** (include) in the **VRChat用のギミック** (VRChat gimmicks) section on page 2 of the expression board, **除外するオブジェクト** (objects to leave out). Keeping a gimmick in the VRM does not reproduce how it works in VRChat. The choice is saved in the avatar's export settings. The expression board applies the choice to the preview when you leave the page.

## Requirements

| Component | Supported version |
| --- | --- |
| Unity | 2022.3; use the version recommended by VCC / ALCOM for your VRChat project |
| lilToon | Any version from 0.12.0-beta.4; the app draws it as lilToon 2.3.4, so 2.3.4 is recommended (0.12.0-beta.1–beta.3 accept 2.0.0–2.3.4, stable 0.11.15 supports 2.3.4 only); install it with your avatar before exporting, through VCC / ALCOM or from the unitypackage under `Assets` |
| UniVRM / UniGLTF | Not required; UniVRM 0.131.4 is bundled with renamed namespaces and shaders, so an installed UniVRM of any version does not conflict |
| VR Vlog | 0.1.1 (build 345) or later for basic lilToon import; newer features require a compatible app version |
| VRChat SDK | Optional; checked with 3.5.0 or later and below 3.11.0, including pre-releases below 3.11.0. Expression files with VRC Animator Play Audio need 3.5.2 or later and below 3.11.0 |
| NDMF | Optional; a stable release from 1.6.0 up to (but not including) 2.0.0 when the avatar uses Modular Avatar or another NDMF tool; the pre-releases 1.15.0-beta.1 and beta.2 also pass for Modular Avatar 1.19.0-alpha.2 |
| Modular Avatar | Optional; 1.12.2 or later and below 1.19.0, or the verified pre-release 1.19.0-alpha.2, when the avatar uses Shape Changer, Object Toggle, Material Setter, Material Swap or Mesh Cutter. Other avatars export with any version |

Install Modular Avatar and its dependencies beforehand if your avatar uses them. The [dependency policy](Compatibility/dependencies.json) records supported versions; the exporter reports missing or unsupported packages without automatically changing them.

The installed lilToon version does not stop an export. 1.x, versions newer than 2.3.4 and pre-releases export too, and the export result notes that the app draws the materials as lilToon 2.3.4. The app always draws lilToon materials with 2.3.4, whatever version the project has, so use 2.3.4 if Unity should look the same as the app.

Before each export the exporter compares the lilToon shaders the avatar uses with 2.3.4 and lists the differences as warnings on the export screen, then continues: properties that 2.3.4 does not have are left out, a material that lacks a 2.3.4 property or has one with another type or texture kind is exported as an MToon approximation (shown as approximated in the export result), and passes are written as in 2.3.4. Only two cases stop: lilToon is not installed, or its bake shader `Hidden/ltsother_baker` differs from 2.3.4. Materials made in a version before 2.3.4 export with the look they have after updating lilToon to 2.3.4. See [supported lilToon versions](Compatibility/README.md#supported-liltoon-versions) for differences such as backlight alpha and the known differences with lilToon 2.4.x.

## Installation

1. Open the [installation page](https://vrvlog.fun/exporter/) and select **VCC / ALCOMに追加** (add to VCC / ALCOM).
2. Open your Unity project's package manager in VCC / ALCOM.
3. Install **VR Vlog lilToon VRM Exporter 0.11.15** from the stable versions.

If the button does not open your package manager, add this repository URL manually:

```text
https://vrvlog.fun/exporter/index.json
```

Existing personal and organization repository registrations remain supported. Keep the current registration; no migration, second registration or reinstall is required. All listing endpoints are synchronized after approved publication.

For manual installation, use **ZIPで導入** on the installation page. Download the exporter ZIP and extract it into a folder under the project's `Packages` directory, with its `package.json` directly inside that folder.

Back up your Unity project before updating. Re-export from the original avatar to apply exporter changes to an existing VRM.

Previous stable **0.11.14** fixes exports stopping during neutral expression sampling when FX and another Playable Layer, such as Locomotion, use the same blend shape. Affected expression groups retain their current settings on the export copy, with the targets and reasons reported as warnings. Independent neutral expressions are still evaluated; the original avatar and controllers are unchanged.

The export details and failure windows offer **Copy full text** and **Save text**. Both include every diagnostic and the complete retained exception, including inner exceptions and stack traces. Text files use UTF-8. Full text includes avatar names and paths; the separate **Copy shareable diagnostics** action keeps its redacted report.

Stable 0.11.15 includes QR transfer and its guide, as adopted from beta.2. Previously published stable 0.11.14 remains export-only. QR transfer requires a compatible receiving app. Check the [Japanese release guidance](README.md#インストール) and [cloud transfer guide](Documentation~/CloudTransfer.md) for the supported app and platform before using it.

## Quick start

The steps below describe stable 0.11.15, adopting beta.2's UI. Previously published ZIPs are unchanged. The UI follows the editor locale; the labels below match the Japanese guide.

1. Open your avatar in Unity. Enable the avatar, its parents and the outfit you want to export.
2. Select **VR Vlog → VRMを書き出す**. Not yet released: the entry reads **VRMを書き出す...**, and choosing **VR Vlog → VRMを書き出す...** from the avatar root's context menu in the Hierarchy opens the window with that avatar assigned (you still press **書き出しを準備**).
3. Assign the avatar's root object from the Hierarchy and select **書き出しを準備**.
4. Every expression found on the avatar starts in **その他の表情**, which is exported as VRM custom expressions. Drag a face onto one of the five VRM expression slots (happy, angry, sad, relaxed, surprised), or right-click it to move it. Move faces you do not want into **書き出さない表情**. Click a face to preview it. Drop more .anim files from Project onto **その他の表情**. Expressions are optional; model-only export is supported.
5. Slots filled from names are marked **自動**; confirm them all with **自動の候補○件を確定**. A slot's header menu also offers a BlendShape or **なし**, and × clears a slot. Unassigned slots stay empty.
6. The board has three pages: **1 表情の設定** (expressions), **2 除外するオブジェクト** (objects to leave out) and **3 VRMの情報** (VRM info). Add optional body poses and check blinking on page 1, then press **次へ：除外するオブジェクト**. Review VRChat gimmicks and objects to leave out on page 2, then press **次へ：VRMの情報**; changed exclusions are applied to the preview before page 3 opens. On page 3, set the author and original license terms under **ライセンス設定**, then select **VRMを保存**. **戻る** goes back one page. In versions with cloud transfer enabled, page 3 is **3 VRMの保存と転送** (save and send the VRM) and the page 2 button reads **次へ：VRMの保存と転送**: page 3 shows the same name, author and **VRMの利用条件** fields as the normal transfer, with **VRMを保存** and **QRコードを作る** side by side. Saving does not need the terms consent. Opened from **VRMを書き出す**, **VRMを保存** is the blue button.

When the body poses exceed the app's limits (128 poses, 32,768 frames of moving poses in total, 16MB of moving pose data, 2MB of still pose data), the **アプリの上限を超えた項目** (items over the app's limits) window opens once during the export. By default it leaves out poses from the end of the list; choose the poses to keep and press **この内容で書き出す** (export with this). The choice is saved in the avatar's export settings and used by later exports, including **アプリに転送（かんたん）**. Poses left out are listed as **同梱ポーズ** (bundled poses) in the export result list. Within the limits the window does not open.

The author is a required VRM field. While it is empty or only spaces, **VRMを保存** stays disabled and the line under the field reads "VRMには作者名が必要です". The author used for a save is remembered per avatar in this PC's Unity Editor preferences and filled in the next time you choose the same avatar. For an avatar without a remembered author, the field keeps what you typed. Only the author name is stored, and nothing is written to the avatar, the project or the VRM. Avatars in an unsaved scene are not remembered.

See the [VRM export guide](Documentation~/ExperimentalExpressionCapture.md) for file support and capture ranges. In 0.11.15, select **QRコードでスマホに送る** in the result section, read the linked privacy policy, check consent, then press **アップロードしてQRを表示**. Scan the QR in a compatible iPhone version of VR Vlog using **モデルを変更 → PCから受け取る**. See the [cloud transfer guide](Documentation~/CloudTransfer.md) for requirements and transfer limits. Consent persists between sessions; opening the transfer window never uploads automatically. Android receiving remains disabled.

Not yet released: in versions with cloud transfer enabled (pre-releases and stable versions that adopt the transfer), right-click the avatar root in the Hierarchy and choose one of two entries under **VR Vlog**. The **VR Vlog** menu in the menu bar has the same entries and transfers the avatar selected in the Hierarchy. Both are greyed out unless exactly one avatar root is selected. Both menus list **アプリに転送（かんたん）...**, **アプリに転送（通常）...**, a separator and **VRMを書き出す...** in that order. Other versions do not show the transfers.

- **アプリに転送（かんたん）** (quick): confirm the name, author and terms consent, then press **QRコードを作る**. The name and author start from the last transfer of this avatar. It exports exactly what the normal flow exports when nothing on the expression board is changed (the expressions last assigned on the board for this avatar, automatic where none, and the objects excluded earlier); the only difference is that the export result list is not shown. Problems that stop an export still stop it with the reason. **表情とオブジェクトを選ぶ** switches to the normal flow.
- **アプリに転送（通常）** (normal): the expression board opens and prepares the avatar. Assign expressions, press **次へ：除外するオブジェクト** and add objects to leave out, then press **次へ：VRMの保存と転送** (the exclusions are applied to the preview first). The transfer form appears on the board's third page, 「3 VRMの保存と転送」; press **QRコードを作る** there (it is the blue button; **VRMを保存** to its left saves the same VRM). **戻る** at the left of the buttons returns to the second page. While uploading or showing the QR, **戻る** first asks "このQRコードは使えなくなります" (this QR code will stop working): **中止して移動** cancels the transfer and leaves, **転送を続ける** stays. Closing the board cancels the transfer in progress without asking.

Required input is the name, the author, consent to the terms (every time) and the privacy policy (first time only). The license is optional: open **VRMの利用条件** to change it, otherwise the last settings or the defaults are used.

The transfer window goes from export to upload and a QR code in one window. Scan that QR with the phone's own camera; it needs a version of VR Vlog that imports from the OS camera. The quick transfer exports what the normal flow exports from an untouched expression board (see above). The expression board, and so the quick transfer, fill empty neutral from VRChat menu expression names (Japanese names included) or whole-face BlendShape names in English, fill empty gaze presets only from names that move both eyes (such as `lookUp` or the Perfect Sync pair `eyeLookUpLeft` + `eyeLookUpRight`), and list each preset filled from a name, mouth shapes included, as a warning. The VRM look-at still uses the eye bones, so most apps do not drive the gaze presets on their own. See the [cloud transfer guide](Documentation~/CloudTransfer.md#アプリに転送).

Textures retain their aspect ratio and are reduced to fit within 1024 × 1024. Smaller textures are not enlarged. The original avatar and textures remain unchanged.

To import on iPhone, save the `.vrm` in Files, enable **lilToon専用表示** in VR Vlog settings, then choose **モデルを変更 → 端末のVRMを選ぶ**. Reload the model after changing the rendering setting.

### App preview

Not yet released: to see a saved VRM as the VR Vlog app loads it, select **アプリでの見え方を確認** after saving. The window has no menu entry; use its **VRM ファイルを開く** to open another `.vrm`. The window shows whether the materials render with lilToon or which ones fall back to MToon, and can switch to standard MToon only. It also applies each VRM expression, plays expression animations, blinks (a test signal for this window only) and sways the avatar to move its spring bones. lilToon is drawn with the official Built-in shaders, so small details can differ from the app's URP shaders. The preview works only when the VR Vlog app's reader package (`com.vrvlog.avatar-runtime`) is installed in the project. That package is not distributed publicly. Without it the button after saving is hidden and exporting works as before. Only `Editor/AppPreview/AvatarRuntimeBackend.cs` (the `VRVlog.LilToonExporter.AppPreview` assembly) references the package; the window uses only the `IAppPreviewBackend` interface in `Editor/AppPreviewEntry.cs` that this file registers. If the package moves or is renamed, that file and its assembly definition are the only ones to change.

## Documentation

Most detailed user guides are currently in Japanese. The compatibility guide and schema files provide additional development references.

| Topic | Reference |
| --- | --- |
| Full user guide and troubleshooting | [Japanese README](README.md) |
| Avatar appearance and SpringBones | [Avatar preservation](Compatibility/AvatarPreservation.md), [PhysBone conversion](Compatibility/PhysBone.md) |
| Body and finger poses | [Humanoid poses](Documentation~/HumanoidPoses.md), [animations](Documentation~/HumanoidAnimations.md) |
| Dependency maintenance and Unity testing | [Compatibility guide](Compatibility/README.md) |
| VRM extension formats | [Schema directory](Schema/) |
| Source layout and local checks | [Contributing guide](CONTRIBUTING.md) |

## Help and contributions

Report vulnerabilities using the [private reporting instructions](SECURITY.md). Keep vulnerability reproduction details and exploit conditions out of public issues and pull requests.

Use **VR Vlog → VRMを書き出す** to inspect installed package versions. Export details explain unsupported items and possible next steps.

[Report a bug or propose an improvement](https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/issues/new/choose) with the relevant versions, reproduction steps and a short sanitized error excerpt. Please keep private avatar assets, credentials and full logs out of public reports. Documentation fixes and pull requests are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Exporter code is available under the [MIT License](LICENSE). Third-party notices cover [lilToon-derived shader code](ThirdPartyNotices/lilToon.md), [UniVRM / UniGLTF](ThirdPartyNotices/UniVRM.md), [Bouncy Castle / ZXing.Net for transfer](ThirdPartyNotices/LanTransfer.md), and the [explicit Avatar Optimizer correction patch](Documentation~/DependencyPatches/README.md) with its [license](Documentation~/DependencyPatches/AAO-LICENSE.txt). Stable 0.11.15 includes the pinned transfer dependencies.

Avatar assets and published images retain their owners' terms. See the [image sources and credits](Website/assets/README.md).
