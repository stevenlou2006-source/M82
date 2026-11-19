# M82

把 **Barrett M82 反器材步枪**加进 R.E.P.O.：0.3 秒一发的连射、单发 500 伤害、2.0 倍镜头晃动。

交流：**#腾讯频道：pd77525911**

---

## 参数

| 项 | 值 |
|---|---|
| 伤害 | **500**（仅对敌人；不对玩家造成伤害，防误伤）|
| 射击间隔 | **0.3 秒** |
| 备弹 | **5 发**（电池 5 格，打完换电池）|
| 价格 | 基础 20,000~22,500（商店按层数倍率 → 约 8 万~9 万）|
| 后坐力 | **12** |
| 镜头晃动 | **2.0** |
| 散布 | **0**（指哪打哪）|
| 体积 | large_high |

---

## 功能

| 功能 | 说明 |
|---|---|
| **无散布** | 子弹严格沿准星方向飞行 |
| **强后坐力反馈** | 枪身后坐 12、镜头晃动 2.0，手感更接近反器材步枪 |
| **固定音效** | 枪声原速播放（Pitch 1.0），无随机音高 |
| **自定义枪身 Shader** | 打进包内的自写 shader，不依赖游戏内置 shader（避免材质变紫红）|

---

## 依赖

| 依赖 | 说明 |
|---|---|
| BepInEx 5.4.x | 必需 |
| [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/) **4.3.0** | 必需（加载 `.repobundle` 并注册物品）|

---

## 安装

用模组管理器安装即可。手动安装时把 `plugins/M82.repobundle` 放进 `BepInEx/plugins/cn_xc-M82/`。

---

## 已知限制

- 仅单人游戏测试，联机未验证

---

## 模型来源与许可证

| 项 | 内容 |
|---|---|
| 模型 | **Barrett M82** |
| 作者 | **Nollien**（Sketchfab）|
| 原页面 | https://sketchfab.com/3d-models/barrett-m82-dcc4d17e171f49e8808ea5e18cdaa038 |
| 许可证 | **Creative Commons Attribution (CC BY)** |

> 依据 CC BY，使用本模型需注明作者与原链接。

<!-- donate-start -->
## 支持作者 / Support

如果这个模组对你有帮助，欢迎[扫码打赏](https://stevenlou2006-source.github.io/cn_xc-media/)——所有打赏只会用于更新与优化模组，不会有其他用途。

If this mod helps you, feel free to [buy me a coffee](https://stevenlou2006-source.github.io/cn_xc-media/) via WeChat Pay. All tips are used only to update and improve my mods.
<!-- donate-end -->
