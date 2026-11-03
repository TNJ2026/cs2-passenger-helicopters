# 发布操作 / Publication workflow

账号、认证信息、发布配置和内部上传记录不进入仓库。以下流程只描述通用构建与校验步骤。

Account credentials, authentication data, publication configuration, and internal upload records are not stored in the repository. This document covers only the generic build and validation workflow.

## 构建与校验 / Build and verify

从本项目根目录运行 / From this project root:

```powershell
./tools/build-rotor-shader.ps1 -Preview
./build.ps1 -Configuration Release -Stage
npm test
node publication.mjs --publish-config <本地配置路径> --output artifacts/publishing/PassengerHelicopters-local
./Check-Publication.ps1 -PackagePath artifacts/publishing/PassengerHelicopters-local
```

默认原生资源来源是本项目的 `assets/native/ImportedData`，不依赖当前游戏、订阅缓存或 MCP 项目。可通过 `--user-data` 指定其他经过校验、包含ImportedData的目录。默认构建来源为 `artifacts/staged`；输出目录必须尚不存在。仅生成和校验不会安装或上传。

The native source defaults to this project's `assets/native/ImportedData`, independently of the live game, subscription cache or MCP checkout. Override with `--user-data` for a verified directory containing ImportedData. Staged code defaults to `artifacts/staged`. Outputs must be fresh directories; packaging and validation do not install or upload.

旋翼着色器使用游戏配套Unity2022.3.62f2构建为Windows AssetBundle，并内嵌运行库DLL。`tools/build-rotor-shader.ps1 -Preview`还会用与运行库相同的几何提取代码检查原生UInt32刚性骨骼流，并用原始桨叶运行D3D11离屏预检。编译后的资源及输入哈希保留在assets/rendering中；运行游戏不需要Unity编辑器。工程预检不代替游戏内HDRP集成、观感和帧率验收。

The rotor shader is built with the game-matchedUnity2022.3.62f2 into a Windows AssetBundle embedded in the runtime DLL. `tools/build-rotor-shader.ps1 -Preview` checks native UInt32 rigid skin with the actual runtime mesh builder and renders the original blades in a D3D11 offscreen preflight. The compiled bundle and input hashes are under assets/rendering; players do not need Unity Editor. These checks do not certify in-game HDRP integration, appearance or frame rate.

发布目录的 `content/`包含两份托管DLL、两份Windows Burst文件、原生资产及玩家文档。配置、图库和清单位于其外。保持Prefab名称、CID及运行库AssemblyVersion，避免破坏旧存档。逐文件哈希和依赖闭包通过不等于实机订阅验收。

The `content/` directory contains managed and Windows Burst binaries, native assets and player documentation. Metadata, gallery and manifest remain outside it. Preserve prefab names, CIDs and runtime AssemblyVersion. Hash and dependency validation does not certify in-game subscription loading.

## 上传与更新 / Upload and update

`Publish.ps1 -PackagePath <目录>`默认只校验；实际操作使用官方 Mod Publisher。账号和认证由官方登录流程管理，不写入项目。发布配置必须位于仓库外或处于忽略状态，可通过 `--publish-config` 或 `CSII_PUBLISH_CONFIG` 传入。

`Publish.ps1 -PackagePath <directory>` validates by default. Uploads use the official Mod Publisher. Its login flow manages account authentication; never store it in the project. Keep the publication configuration outside the repository or ignored, and pass it with `--publish-config` or `CSII_PUBLISH_CONFIG`.

- 新代码版本 / New code version: 更新项目版本与本地发布配置中的发行版本，准备并验证新目录，再使用 `./Publish.ps1 -PackagePath <目录> -Command NewVersion`。
- 图片、名称、描述或可见性 / Artwork, name, descriptions or visibility: `./Publish.ps1 -PackagePath <目录> -Command Update`，不重新创建代码版本。
- 新条目 / New listing: 仅在明确需要新条目时清空本地配置中的 ModId 并使用 Publish。

上传后实读默认详情，核对用户版本、平台版本、Published、访问级别、封面和四张截图。Private 转 Public 时也要核对默认列表元数据。不要用重复 NewVersion 修复展示资料。

After upload, read the default details and verify the user version, platform version, Published state, visibility, cover, and all four screenshots. Check the default listing metadata after a Private-to-Public transition as well. Do not submit another NewVersion to repair artwork metadata.

## 发布图片 / Artwork

封面为新生成的正方形宣传图，1024×1024 JPEG；它不是实机截图。四张真实展示图来自用户提供的机场、停机坪、菜单及线路管理画面，压缩为1920×1080 JPEG。发布源在Properties，来源及哈希在Artwork.json；封面原图和提示词在assets/helicopter/publication。原始用户截图来源仍保留在tf3项目，当前项目内已有完整发布副本。

The cover is a generated1024×1024 square promotional illustration. Four real gameplay screenshots show the heliport, helipad, menu and line management at1920×1080. Publication copies and provenance are under Properties; the generated source and prompt are under assets/helicopter/publication. The original screenshots remain in the separate tf3 project; publication does not depend on that folder.

