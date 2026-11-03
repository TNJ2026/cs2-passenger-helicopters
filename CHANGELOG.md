# 更新记录 / Changelog

## 1.1.10 候选 — 程序集隔离 / Assembly isolation candidate

- 将运输、模型、渲染与界面逻辑迁入 `TransportFever3Helicopters.Runtime.dll`；`CityWeaver.dll` 缩减为只保留旧存档和Prefab序列化身份的兼容层。MCP继续为可选适配，不成为运行依赖。
- Move simulation, models, rendering and UI into `TransportFever3Helicopters.Runtime.dll`; reduce `CityWeaver.dll` to the legacy save/prefab identity layer. MCP remains optional and is not a runtime dependency.

## 1.1.9 — 新建机场自动标高与地形范围 / Automatic heliport grade and terrain bounds

- 已发布至原Paradox Mods条目165769，平台版本7、Public、Published；默认详情、中英文新功能说明及四张截图已核验。
- Published on the existing Paradox Mods item165769 as platform version7, Public and Published; default details, bilingual new-feature descriptions and four screenshots verified.

- RC2：整平核心不再向机场104×112米边界外额外延伸；平滑过渡从四周8米缩至2米，外包范围由120×128米缩为108×116米。创建模板和已导入原生预制体统一覆盖旧参数。坡地仍需要过渡带，无法承诺边界外完全无地形变化。
- RC2: Keep the level core inside the104×112m apron and reduce the exterior blend from8m to2m per side (108×116m instead of120×128m). Apply the same settings to generated and imported prefabs. A narrow terrain transition remains necessary on uneven ground.

- 原生手动新建双机位机场时，在侧后方整形范围外采样九点；至少六点与中位数相差不超过0.75米、总体高差不超过4米且平台需要抬高0.15至4米时，采用该场地高度。坡地、异常数据及大幅填挖保留原生标高，不固定增加2.19米。
- Before native manual heliport preview generation, sample nine points beyond the side/rear terraform apron. Use the median only with six supporting samples within0.75m, a total range at most4m and a required raise of0.15–4m. Keep native placement on slopes, invalid data or larger earthworks.
- 建筑、登机点与内部网络定义同步抬高，临路步行入口保持原生高度并通过坡道衔接；碰撞和接驳仍由原生预览校验。已有建筑、搬移、编辑器和MCP指定标高不自动修改。
- Lift building, stop and internal-network definitions together; retain the native pedestrian frontage height and interpolate the connecting ramp. Native preview validation remains active. Existing buildings, moves, the editor and explicit MCP elevations are unchanged.
- 自动标高、道路接驳及RC2重启后的108×116米整形参数已在本机检查，重载后机场标高与两条线路有效性保持。46项回归及Release构建通过。反复旋转预览、更多场地及实际上下客仍需继续验证。保持程序集兼容身份和原生资源CID。
- Local checks passed for grade selection, road access and the108×116m RC2 terraform settings after restart; reloaded airport elevation and both lines remained valid. All46 regressions and the Release build passed. Repeated preview rotation, more sites and actual passenger service need further validation. Preserve assembly compatibility and native resource CIDs.

## 1.1.8 — 旋翼动画与转速优化 / Rotor animation and RPM transitions

- 已更新到原Paradox Mods条目165769，平台版本6、Public、Published；默认详情、双语说明、正方形封面及四张截图核验通过。
- Published on the existing Paradox Mods item165769 as platform version6, Public and Published. Default details, bilingual descriptions, square cover and four screenshots verified.

- 1.1.7实机验收读取到Rendering队列中旋翼系统出现两次（索引635和652），降速实际约3秒而非6秒、提速约1.5秒而非3秒，显示相位也重复推进；1.1.7验收未通过。
- Live1.1.7 inspection found the rotor system twice in the Rendering queue (indices635 and652). Idle-down took about3 rather than6 animation seconds, spool-up about1.5 rather than3, and visual phase advanced twice. Version1.1.7 failed acceptance.
- 删除重复的相对登记，只在ProceduralSkeletonSystem前登记一次；原生队列已经把ObjectInterpolateSystem放在此前。增加同一Unity渲染帧的幂等保护，避免额外调用再次推进转速、相位与巡航曝光计时。
- Register only once before ProceduralSkeletonSystem, which already follows ObjectInterpolateSystem in the native queue. Add a per-Unity-frame guard so extra calls cannot advance power, phase or cruise exposure timers again.
- 新增直接编译生产Load方法的调度回归，并重现旧重复登记以确认测试能够拒绝它。保留65%怠速、6秒降速、3秒提速、350公里/小时、原生运输及资产身份；795文件核验及本机运行验收通过，用户确认过渡自然，已公开发布。
- Add a scheduling regression compiling the production Load method, including rejection of the exact old duplicate registration. Keep65% idle,6-second idle-down,3-second spool-up,350km/h, native transport and asset identities. Installed795-file verification and live acceptance passed; the user accepted the natural transition. Publicly released.

## 1.1.7 — 地面旋翼转速过渡候选 / Ground rotor power transition candidate

实机验收发现重复调度，转速和过渡计时加倍；未通过验收，由1.1.8修正。 / Failed live acceptance due to duplicate scheduling that doubled visual speed and transition timing; addressed in1.1.8.

- 仅在确认静止、落地上下客且已知离站计划允许时，约6个原生动画秒内将显示转速降至65%；离站前4秒开始提速，约3秒恢复全速。未知、过期或提前结束的上下客不增加发车等待。
- During confirmed stationary ground boarding with sufficient scheduled dwell, ease displayed RPM to65% over6 native animation seconds. Start a3-second spool-up4 seconds before scheduled departure. Unknown, expired or early boarding completion never adds dispatch waits.
- 起飞、进场、空中和降落保持原有全速；主尾旋翼共用动力比例，连续积分显示角度。使用与原生相同的插值时间，暂停冻结，随游戏速度运行；不写入原生动量、预制体转速或运输参数。
- Retain full RPM for takeoff, approach, flight and landing. Main and tail share the power envelope with continuous visual phase, using native interpolation time for pause and simulation-speed behavior. Do not write native momentum, prefab RPM or transport parameters.
- 保留起降清晰桨叶、巡航48个角度样本、350公里/小时与存档身份。安装后795文件核验通过，但实机验收因重复调度未通过；未上传，GPU耗时/FPS对照未测量。
- Preserve clear maneuver blades,48 cruise samples,350km/h and save identities. Installed content passed795-file verification, but live acceptance failed due to duplicate scheduling. Not uploaded; GPU time/FPS comparison unmeasured.

## 1.1.6 — 起降实桨候选 / Clear blades during maneuvers

- 起飞、进场、降落、地面、登机、暂停和低于90km/h时，立即关闭模组角度拖影，不再保留约0.6秒淡出残影。
- Immediately remove the mod's angular trails on takeoff, approach, landing, ground movement, boarding, pause and at or below90km/h, without the previous0.6-second fade-out.
- 增加爬升/下降速度判断：即使原生起降标记提前解除，明显升降时仍保持实桨；平飞稳定约1秒后才渐入已验收的巡航拖影。
- Keep clear blades during vertical climb/descent even if native maneuver flags clear early; resume the accepted cruise trails only after about1 second of steady level flight.
- 使用真实暂停/平滑速度状态，避免加速运行时将插值停顿误当作暂停。保留48角度样本、350km/h巡航、运输和存档身份。
- Read actual pause/smoothed simulation speed rather than treating stalled interpolation as pause. Preserve48 samples,350km/h cruise and transport/save identities.
- 当前相机启用原生运动模糊时，起降改用后处理后的清晰实桨通道，只绘制当前位置，不绘制拖影样本；其他物体继续使用玩家原有全局效果。
- When native camera motion blur is active, draw only the current real blades after postprocessing during clear phases, retaining the player's global effect on other objects.
- 已安装并通过本机起降观感验收，巡航恢复和线路运行正常；GPU耗时/FPS对照未测量，尚未上传。 / Installed and locally accepted for maneuver appearance, cruise recovery and line operation; GPU time/FPS comparison unmeasured. Not uploaded.

## 1.1.5 — 原生展开网格兼容候选 / Native unrolled-mesh compatibility candidate

- 修正1.1.4实机载入时展开的旋翼角点乘以48次采样后超过顶点预算，导致新效果未启用的问题。按同一旋翼和材质内完全相同的位置、UV合并顶点，保留贴图接缝，不提高预算或减少桨叶三角形。
- Fix angular exposure failing to initialize when native unrolled triangle corners exceeded the vertex budget in1.1.4. Weld identical position/UV pairs within each rotor/material, preserving UV seams and blade triangles without raising budgets.
- 使用与已发布GeometryAsset头部一致的六个原生材质网格布局做回归，包含展开顶点、Float16 UV及交错UInt32骨骼索引，并验证主尾旋翼与GPU绘制。保持48个角度样本、350公里/小时、全部模型及存档身份。
- Add regression/GPU checks using six native material layouts whose counts match the shipped GeometryAsset, with unrolled vertices, Float16 UVs and interleaved UInt32 bone indices. Keep48 angular samples,350 km/h and model/save identities.
- 本机确认旋转拖影自然，起降、登机和暂停恢复检查通过；GPU耗时/FPS对照未测量，尚未上传。 / Locally accepted rotor appearance, with maneuver/boarding/pause checks passed; GPU time/FPS comparison unmeasured. Not uploaded.

## 1.1.4 — 真实桨叶角度采样候选 / Real-blade angular sampling candidate

本机载入发现原生展开顶点超过预算，实际回退实桨，视觉验收未通过；由1.1.5修正。 / In-game initialization exceeded the budget on unrolled native vertices and fell back to native blades; visual acceptance failed. Addressed in1.1.5.

- 针对1.1.3本机验收的直线重影，增加旋翼专用角度采样：用原始桨叶几何和贴图沿主尾转轴累积48个较淡样本，不使用模糊桨盘。
- Address straight ghosting reported in local1.1.3 testing with48 weighted angular shutter samples of the original blade geometry and textures, without synthetic disks.
- 起降、地面、低速、上下客保留清晰实桨，巡航平滑过渡；暂停立即恢复实桨。新通道在原生运动模糊之后绘制，避免再次被拉成长直线；无需启用全局运动模糊。
- Keep clear blades during maneuvers, ground movement, low speed and boarding; blend smoothly in cruise and restore clear blades on pause. Draw after native motion blur to avoid reblurring the angular samples; global motion blur is optional.
- 缓存几何和材质，仅处理近处可见载具，最多8架；渲染不可用或超出预算时恢复原始桨叶。修正原生诊断读取默认VolumeStack而非当前相机VolumeStack的问题。
- Cache geometry/materials, limit the effect to8 nearby visible aircraft and retain native blades if rendering is unavailable or over budget. Correct diagnostics to read the active camera's volume stack.
- 保留模型CID、旧程序集身份、线路配色、60座与350公里/小时。此版本仅为本机候选，GPU离屏预检不替代游戏内观感及性能验收，尚未上传。
- Preserve model CIDs, legacy assembly identity, route colors,60 seats and350 km/h. Local candidate only; offscreen GPU checks do not certify in-game visuals or performance. Not uploaded.

## 1.1.3 — 原生旋翼运动模糊候选 / Native rotor motion blur candidate

- 移除主尾旋翼模糊桨盘、几何缩放切换及人为慢转控制，始终使用原模型的真实桨叶和原生PoweredRotation。
- Remove main/tail blur disks, geometry-scale switching and synthetic slow rotation. Always use the original real blades and native PoweredRotation.
- 使用游戏已有的程序骨骼前后帧矩阵及运动矢量通路，新增只读诊断，报告材质MotionVectors通道、骨骼历史和后处理状态。
- Use the game's procedural bone history and motion-vector pipeline, with read-only diagnostics for the rotor material MotionVectors pass, bone history and post-processing state.
- 效果依赖游戏图形设置中的运动模糊。模组不强制修改全局设置；关闭该选项时仍显示实桨，不回退桨盘。此候选仍需安装后验证实际拖影效果。
- The effect requires the game's graphics motion-blur option. Global settings are not forced; disabling it keeps real blades visible. This candidate requires in-game validation of the actual blur.
- 发布内容恢复为392个原生资产；保留原模型及存档身份、线路配色、60座和350公里/小时。
- Return to392 native assets, retaining original model/save identities, route colors,60 seats and350 km/h.

## 1.1.2 — 起降桨叶与动态桨盘 / Flight-phase rotor visuals

- 修正1.1.1本机测试版静态桨盘和低速起降仍显示模糊桨盘的问题；加入原生起飞、进场、降落及登机状态判断。
- Correct the static blur disks and blurred low-speed takeoff/landing in the local 1.1.1 test build; recognize native takeoff, approach, landing and boarding states.
- 起降、地面及低于90公里/小时显示可辨认的旋转桨叶；正常飞行在90至160公里/小时之间平滑切换，160公里/小时以上巡航显示缓慢转动的模糊桨盘。
- Show readable rotating blades during maneuvers, on the ground and below 90 km/h; transition during normal flight between 90 and 160 km/h, with visibly rotating blur disks above 160 km/h in cruise.
- 单独维护每架飞机的视觉角度，限制掉帧时的单帧转角；暂停冻结、恢复续转，加速模拟不会放大视觉转角。仅修改显示，不改原生旋翼动量、350公里/小时巡航、运输逻辑、存档身份或资产CID。
- Maintain per-vehicle visual phases with bounded frame steps, pause/resume continuity and no simulation-speed multiplier. Native rotor momentum, 350 km/h cruise, transport behavior, save identities and asset CIDs are unchanged.

## 1.1.1 — 旋翼视觉修复 / Rotor visual fix

- 恢复原模型带透明贴图的主旋翼、尾旋翼模糊桨盘；按每架飞机的实际旋翼动量，在低速实桨与高速桨盘之间平滑改变显示比例，减少高速清晰桨叶的频闪。
- Restore the original alpha-textured main and tail blur meshes. Select and smoothly scale the real/blur representations per vehicle from actual rotor momentum to reduce high-speed blade strobing.
- 模糊桨盘固定于原始转轴基准，避免对已经积分一整圈的贴图再次高速旋转；模糊资源未就绪时保留实桨。
- Hold the integrated blur image at its authored basis to avoid strobing it again, and retain real blades if the blur skeleton is unavailable.
- 保留程序集和既有模型CID、线路和存档身份、线路配色、60座及350公里/小时。构建和离线检查后仍需游戏重启验收。
- Preserve assembly and existing model CIDs, route/save identities, route colors, 60 seats and 350 km/h. A game restart and in-game verification are required after the build and offline checks.

## 1.1.0 — 公开发布 / Public release

- 使用1024×1024正方形宣传封面，保留四张1080p实机图与三部分双语说明，原模组中心条目转为Public。
- Publish the existing mod item with a 1024×1024 square promotional cover, four 1080p gameplay images and bilingual descriptions organized into three sections.
- 包含完整的独立直升机客运与管理，保留既有模型、存档身份、菜单/人行修复、60座和350公里/小时配置。
- Include standalone helicopter transport and management, preserving models, save identities, menu and pedestrian fixes, 60 seats and 350 km/h.

## 1.0.9 — 名称与发布资料 / Name and publication materials

- 名称统一为Transport Fever 3 Helicopters / 狂热运输3直升机客运，程序集标题同步移除括号后缀。
- Synchronize the name and assembly titles as Transport Fever 3 Helicopters / 狂热运输3直升机客运.
- 使用之前生成的宣传图作封面，图库更新为机场、独立停机坪、建造菜单和线路管理四张1920×1080实机截图。
- Use the generated promotional cover and four 1920×1080 gameplay images showing the heliport, independent helipad, construction menu and line management.
- 中英文描述按简介、功能和如何使用整理，完整安装包包含最新文档与来源记录。
- Organize bilingual descriptions into Overview, Features and How to use, and include current documentation and artwork provenance in the release materials.
- 保留1.0.8运输逻辑、菜单修复、存档身份、60座和350公里/小时。
- Preserve the 1.0.8 transport behavior, menu fixes, save identities, 60 seats and 350 km/h.

## 1.0.8 — 交通分类菜单与提示 / Transport category and tooltips

- 清理订阅机场附带的重复直升机分类菜单入口，修复多出的占位图标标签；按原始名称/CID识别，保留第三方分类及原资源引用。
- Remove the duplicate helicopter category entry bundled with the subscribed airport, fixing the extra placeholder tab. Exact name/CID checks preserve unrelated categories and original asset references.
- 补齐原生分类使用的SubServices.NAME与Assets.SUB_SERVICE_DESCRIPTION中英文文本，保留普通资产文本兼容键。
- Add Chinese/English SubServices.NAME and Assets.SUB_SERVICE_DESCRIPTION entries used by native category tooltips, retaining existing asset text keys for compatibility.
- 不改运输、模型、站点或线路身份；保留1.0.7人行连接修复、60座和350公里/小时。
- Keep transport behavior, models, stop and route identities, the 1.0.7 pedestrian fix, 60 seats and 350 km/h.

## 1.0.7 — 订阅登机点迁移 / Subscribed boarding-marker migration

- 按原始名称和CID识别完整包中序列化的登机点，清理旧步行生成点配置，修复订阅机场“缺少人行道”警告及线路工具标记过滤。
- Recognize the bundled serialized boarding marker by its original name and CID; migrate obsolete pedestrian spawn data and restore subscribed stop markers in the route tool.
- 保留既有建筑、登机点和线路引用，继续排除第三方同名订阅资产；60座、350公里/小时及模型CID不变。
- Preserve building, stop and waypoint identities and exclude unrelated same-name subscribed assets; keep 60 seats, 350 km/h and model CIDs.

## 1.0.6 — 双语补全 / Bilingual completion

- 补齐机场、登机站点和菜单分类描述，建线错误提示改用简体中文/英文本地化键，随游戏界面语言切换。
- Complete airport, boarding-stop and category descriptions; route errors now use Simplified Chinese/English localization IDs and follow the interface language.
- 发布页短说明、更新日志、README、来源及发布说明提供中英文内容，并随完整候选包分发。
- Provide Chinese and English release summaries, changelogs, README, credits and publication instructions in the complete candidate.
- 保留运输逻辑、存档身份、60座及350公里/小时配置；不上传、不替换本地已验收版本，订阅验收仍待进行。
- Keep transport behavior, save identities, 60 seats and the 350 km/h cap. No upload or replacement of the accepted local installation; subscription acceptance remains pending.

## 1.0.5 — 发布候选 / Publication candidate

- 准备不含可选MCP桥接的完整Windows代码与模型发布目录。
- Prepare a complete Windows code-and-model payload, without the optional MCP bridge.
- 按精确Prefab名称和原始CID识别本模组订阅的机场及渲染资源，继续排除第三方订阅内容。
- Accept this mod's subscribed airport and render resources by exact prefab name and native CID; continue excluding unrelated subscribed content.
- 增加双语玩家指南、来源署名、发布资料和打包检查。
- Add bilingual player guides, source credits, publication metadata and packaging checks.
- 保留存档身份、60座、350公里/小时巡航及1.0.3直达进场修复。
- Keep existing save identities, 60-seat capacity, 350 km/h cruise cap and the 1.0.3 direct airport approach repair.
- 原生订阅安装需要单独验收；打包检查不能代表已发布版本通过实测。
- Native subscription installation remains a separate acceptance step; packaging checks do not certify an uploaded release.

## 1.0.4

发布名称改为“狂热运输3直升机客运（模型移植）”，并注明三个模型的来源。

Rename publication metadata to Transport Fever 3 Helicopters / 狂热运输3直升机客运（模型移植） and document the source of all three models.

## 1.0.3

分离重合的空中接口，恢复机场第二机位的直达进场，同时保留旁侧机库通道。

Restore the second airport pad's direct approach by separating collapsed airway connections while retaining the side depot connector.

## 1.0.2

按水平距离提前进场减速，巡航保持350公里/小时。

Add horizontal-distance approach braking; keep 350 km/h cruise speed.

## 1.0.1

修复独立停机坪的空中寻路节点重合，并改进可选MCP的线路预览诊断。

Repair collapsed standalone helipad airway nodes and improve optional MCP preview-path diagnostics.

## 1.0.0

将模型、客运逻辑和管理功能拆分为独立直升机模组。

Extract models, passenger transport and management into the independent Passenger Helicopters mod.
