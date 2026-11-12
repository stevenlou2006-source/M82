// M82 → REPO 枪械物品
// 路线：克隆原版 Tranq 枪 prefab → 彻底展开嵌套 prefab → 换掉原版粒子/线材质（避免 Particles/Standard Surface 编译崩溃）
//       → 删掉原版模型 → 换 M82 模型 → 参数微调 → 打 .repobundle
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Photon.Pun;

public static class M82Builder
{
    const string MOD_DIR = "Assets/M82MOD";
    const string GEN_DIR = MOD_DIR + "/Generated";
    const string FBX = MOD_DIR + "/barret.fbx";
    const string BUNDLE = "m82";
    const string VANILLA_GUN = "Assets/REPO/Game/Resources/items/Item Gun Tranq.prefab";

    const float TARGET_LENGTH = 1.15f;
    const float VERTICAL_NUDGE = 0.06f;
    static readonly Vector3 VISUAL_OFFSET = new Vector3(0f, 0f, -0.14f);   // 回退到上一版
    static readonly Vector3 VISUAL_EULER = new Vector3(0f, 0f, 0f);
    const float RECOIL = 12.0f;   // 12.7mm，后坐力调大
    const float BATTERY_DRAIN = 20f;   // 100/20 = 5 发   // 100 / 33.3333 = 3 发（备弹 3）
    const float MUZZLE_Y_RAISE = 0.06f;   // 枪口点上抬（修落点偏下）
    const float COLLIDER_BOX_Z = -0.50f;   // 包围盒后移量
    const float GRAB_POINT_Z = -0.30f;   // 抓取点前移量（枪身相对手后移）
    const float COOLDOWN = 0.3f;   // M82：0.3 秒间隔   // M82：换弹 2.5 秒

    [MenuItem("REPO/生成 M82 枪械包")]
    public static void BuildAll()
    {
        try
        {
            if (!Directory.Exists(GEN_DIR)) Directory.CreateDirectory(GEN_DIR);

            // ---------- 材質 ----------
            // 内置 Standard 在游戏运行时不提供（会变粉红），改用本工程内打包的自写 shader
        var gunShader = Shader.Find("AWPGun/Gun");
        if (gunShader == null) { Debug.LogError("[M82] 找不到 AWPGun/Gun shader"); gunShader = Shader.Find("Standard"); }
        var mat = new Material(gunShader);
            mat.name = "M82 Material";
        // 接入模型自带的颜色贴图
        var albedoPath = "Assets/M82MOD/M82 BaseColor.png";
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        if (albedo != null) { mat.mainTexture = albedo; mat.SetTexture("_MainTex", albedo); Debug.Log("[M82] 已接入颜色贴图: " + albedo.width + "x" + albedo.height); }
        else Debug.LogWarning("[M82] 没找到颜色贴图: " + albedoPath);
            mat.color = new Color(0.22f, 0.22f, 0.24f);
            mat.SetFloat("_Metallic", 0.65f);
            mat.SetFloat("_Glossiness", 0.55f);
            SaveAsset(mat, "M82 Material.mat");

            var partMat = new Material(Shader.Find("QBZ191Gun/Particle"));
            partMat.name = "M82 Particle Mat";
            // 圆形柔化贴图：不设的话每个粒子是白色方块（"马赛克一点一点"）
            var ptex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/M82MOD/Generated/M82 Particle Tex.png");
            if (ptex != null) { partMat.mainTexture = ptex; partMat.SetTexture("_MainTex", ptex); Debug.Log("[M82] 粒子贴图已接上: " + ptex.width + "x" + ptex.height); }
            else Debug.LogWarning("[M82] 没找到粒子贴图（会显示成方块）");
            SaveAsset(partMat, "M82 Particle Mat.mat");

            var lineMat = new Material(Shader.Find("QBZ191Gun/Line"));
            lineMat.name = "M82 Line Mat";
            SaveAsset(lineMat, "M82 Line Mat.mat");
            if (partMat.shader == null || partMat.shader.name != "QBZ191Gun/Particle") Debug.LogWarning("[M82] 自写粒子 shader 没找到，可能渲染异常");
            if (lineMat.shader == null || lineMat.shader.name != "QBZ191Gun/Line") Debug.LogWarning("[M82] 自写线条 shader 没找到，可能渲染异常");

            // ---------- 克隆原版枪并彻底展开 ----------
            var vanilla = AssetDatabase.LoadAssetAtPath<GameObject>(VANILLA_GUN);
            if (vanilla == null) throw new Exception("找不到原版枪 prefab: " + VANILLA_GUN);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(vanilla);
            root.name = "Item Gun M82";

            // 关键：彻底展开所有嵌套 prefab 实例，否则后面换材质只改到外层、原版材质仍会被打包（并触发 shader 编译崩溃）
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "Item Gun M82";
            Debug.Log("[M82] 已彻底展开嵌套 prefab");

            // 换掉所有粒子 / 线条材质（内置 shader，不需要现场编译）
            int psrCount = 0, lrCount = 0;
            foreach (var psr in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (psr == null) continue;
                psr.material = partMat;
                psr.trailMaterial = partMat;
                psrCount++;
            }
            foreach (var lr in root.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr == null) continue;
                lr.material = lineMat;
                lrCount++;
            }
            Debug.Log("[M82] 替换粒子材质 " + psrCount + " 个，线条材质 " + lrCount + " 个");

            // 电池 UI Canvas 保持原版不动：ItemBattery.Update() 每帧都会调用 Canvas 上的 BatteryVisualLogic，
            // 任何删除/隐藏都可能导致空引用（另有 Solar Batteries Fixed 也会改 ItemBattery.Update）。
            // 代价：电池格上的 TMP 文字可能显示异常（TMP shader 未随包），但不影响功能。

            // 删掉原版模型前：先记录原版视觉的局部变换与世界尺寸（用来对齐我们的模型）
            Transform vanillaVisual = null;
            bool hasVanillaBounds = false;
            Bounds vanillaBounds = default(Bounds);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                if (vanillaVisual == null) { vanillaVisual = r.transform; vanillaBounds = r.bounds; hasVanillaBounds = true; }
                else vanillaBounds.Encapsulate(r.bounds);
            }
            Vector3 vPos = vanillaVisual != null ? vanillaVisual.localPosition : Vector3.zero;
            Quaternion vRot = vanillaVisual != null ? vanillaVisual.localRotation : Quaternion.identity;
            float vLen = hasVanillaBounds ? Mathf.Max(vanillaBounds.size.x, Mathf.Max(vanillaBounds.size.y, vanillaBounds.size.z)) : 0f;
            Debug.Log("[M82] 原版视觉: localPos=" + vPos.ToString("F3") + " rot=" + vRot.eulerAngles.ToString("F1") + " 世界长度=" + vLen.ToString("F3") + "m");

            // 删掉原版枪的模型（带 MeshRenderer 的子物体），保留所有功能子物体
            var kill = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                var t = r.transform;
                if (t == root.transform) continue;
                if (!kill.Contains(t.gameObject)) kill.Add(t.gameObject);
            }
            foreach (var g in kill) UnityEngine.Object.DestroyImmediate(g);
            Debug.Log("[M82] 已移除原版模型子物体 " + kill.Count + " 个");

            // 删掉麻醉枪专属组件（会播麻醉枪的声音/特效）
            try
            {
                int removed = 0;
                foreach (var tg in root.GetComponentsInChildren<ItemGunTranq>(true))
                {
                    if (tg == null) continue;
                    UnityEngine.Object.DestroyImmediate(tg);
                    removed++;
                }
                Debug.Log("[M82] 已移除 ItemGunTranq 组件 " + removed + " 个");
            }
            catch (Exception e) { Debug.LogWarning("[M82] 移除 ItemGunTranq 失败: " + e.Message); }

            // 抓取锚点：把「Force Grab Point」往枪口方向挪，让枪身相对手更靠后
            try
            {
                Transform gp = null;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr != null && tr.name == "Force Grab Point") { gp = tr; break; }
                }
                if (gp != null)
                {
                    Vector3 before = gp.localPosition;
                    gp.localPosition = new Vector3(before.x, before.y, before.z + GRAB_POINT_Z);
                    Debug.Log("[M82] Force Grab Point: " + before.ToString("F3") + " -> " + gp.localPosition.ToString("F3"));
                }
                else Debug.LogWarning("[M82] 没找到 Force Grab Point");
            }
            catch (Exception e) { Debug.LogWarning("[M82] 抓取点调整失败: " + e.Message); }

            // ---------- 换上新模型 ----------
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
            if (fbxAsset == null) throw new Exception("找不到 FBX: " + FBX);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);

            // 模型长轴如果不是 Z 轴（枪管方向），记录下来，最后并进 localRotation
            Vector3 axisEuler = Vector3.zero;
            try
            {
                var rb0 = renderers[0].bounds;
                foreach (var rr in renderers) rb0.Encapsulate(rr.bounds);
                Vector3 s0 = rb0.size;
                if (s0.x > s0.y && s0.x > s0.z) { axisEuler = new Vector3(0f, 90f, 0f); Debug.Log("[M82] 模型长轴=X → 旋转 90°(Y)"); }
                else if (s0.y > s0.x && s0.y > s0.z) { axisEuler = new Vector3(-90f, 0f, 0f); Debug.Log("[M82] 模型长轴=Y → 旋转 -90°(X)"); }
            }
            catch (Exception e) { Debug.LogWarning("[M82] 长轴判断失败: " + e.Message); }
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                // 应用轴旋转后重新量包围盒，按真实长轴缩放
                visual.transform.localRotation = Quaternion.Euler(axisEuler);
                var rb2 = renderers[0].bounds;
                foreach (var rr in renderers) rb2.Encapsulate(rr.bounds);
                b = rb2;
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                // 缩放：固定用户认可的 1.00 m（原版视觉包围盒不可靠，实测算出 3.1m）
                float targetLen = TARGET_LENGTH;
                if (longest > 0.001f)
                {
                    float k = targetLen / longest;
                    visual.transform.localScale = visual.transform.localScale * k;
                    Debug.Log("[M82] 模型最长边 " + longest.ToString("F3") + "m → 目标 " + targetLen.ToString("F3") + "m，缩放 " + k.ToString("F4"));
                }
                // 位置/旋转：完全对齐原版视觉
                visual.transform.localPosition = vPos + VISUAL_OFFSET;
                visual.transform.localRotation = vRot * Quaternion.Euler(VISUAL_EULER) * Quaternion.Euler(axisEuler);
            }

            // 体积匹配：不改 m_Size（会抖动），改为等比缩放碰撞体子物体的 transform
            try
            {
                var holders = new System.Collections.Generic.List<Transform>();
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr == null) continue;
                    if (tr.gameObject.name.StartsWith("Semi Box Collider", StringComparison.OrdinalIgnoreCase)) holders.Add(tr);
                }
                if (holders.Count > 0 && renderers.Length > 0)
                {
                    // 模型在世界空间的长度
                    var mb = renderers[0].bounds;
                    foreach (var r in renderers) mb.Encapsulate(r.bounds);
                    Vector3 msizeLocal = root.transform.InverseTransformVector(mb.size);
                    float modelLen = Mathf.Max(Mathf.Abs(msizeLocal.x), Mathf.Max(Mathf.Abs(msizeLocal.y), Mathf.Abs(msizeLocal.z)));

                    // 原版碰撞体联合长度
                    bool has = false; Bounds cb = new Bounds();
                    foreach (var h in holders)
                    {
                        var bc0 = h.GetComponent<BoxCollider>();
                        if (bc0 == null) continue;
                        if (!has) { cb = bc0.bounds; has = true; } else cb.Encapsulate(bc0.bounds);
                    }
                    if (has)
                    {
                        Vector3 csizeLocal = root.transform.InverseTransformVector(cb.size);
                        float collLen = Mathf.Max(Mathf.Abs(csizeLocal.x), Mathf.Max(Mathf.Abs(csizeLocal.y), Mathf.Abs(csizeLocal.z)));
                        if (collLen > 0.02f)
                        {
                            float ratio = Mathf.Clamp(modelLen / collLen, 0.5f, 3f);
                            Debug.Log("[M82] 碰撞体等比缩放 ratio=" + ratio.ToString("F3") + " 模型长=" + modelLen.ToString("F3") + " 碰撞长=" + collLen.ToString("F3") + " 数量=" + holders.Count);
                            foreach (var h in holders)
                            {
                                Vector3 ls = h.localScale;
                                h.localScale = new Vector3(ls.x, ls.y, ls.z * ratio);
                                Vector3 lp = h.localPosition;
                                h.localPosition = new Vector3(lp.x, lp.y, lp.z * ratio + COLLIDER_BOX_Z);   // 包围盒整体后移
                                Debug.Log("[M82]   " + h.gameObject.name + " scale=" + h.localScale.ToString("F3") + " pos=" + h.localPosition.ToString("F3"));
                            }
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[M82] 碰撞体等比缩放失败: " + e.Message); }

            // 注意：不要改原版碰撞体（尺寸/中心算错会导致物理抖动）

            // 让模型底部与碰撞体底部对齐（否则模型会悬空/陷进地面）
            try
            {
                var mb2 = renderers[0].bounds;
                foreach (var r in renderers) mb2.Encapsulate(r.bounds);

                bool hasC = false; Bounds cb2 = new Bounds();
                foreach (var bcx in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (bcx == null) continue;
                    string nm = bcx.gameObject.name.ToLowerInvariant();
                    if (nm.Contains("hurt") || nm.Contains("effect") || nm.Contains("particle")) continue;
                    if (!hasC) { cb2 = bcx.bounds; hasC = true; } else cb2.Encapsulate(bcx.bounds);
                }
                if (hasC)
                {
                    float dyWorld = cb2.min.y - mb2.min.y;
                    Vector3 localShift = root.transform.InverseTransformVector(new Vector3(0f, dyWorld, 0f));
                    visual.transform.localPosition += localShift;
                    Debug.Log("[M82] 模型对齐碰撞体底部: 世界Δy=" + dyWorld.ToString("F4") + " 局部Δ=" + localShift.ToString("F4"));
                }
            }
            catch (Exception e) { Debug.LogWarning("[M82] 模型底部对齐失败: " + e.Message); }

            // 枪口点：贴到模型前端（否则枪口火焰会出现在枪身中段）
            var gunMuzzleT = FindDeep(root.transform, "Gun Muzzle");
            if (gunMuzzleT != null && renderers.Length > 0)
            {
                var wb = renderers[0].bounds;
                foreach (var r in renderers) wb.Encapsulate(r.bounds);
                Vector3 c = root.transform.InverseTransformPoint(wb.center);
                Vector3 s = wb.size;
                int axis = (s.x >= s.y && s.x >= s.z) ? 0 : (s.y >= s.z ? 1 : 2);
                Vector3 pos = c;
                pos[axis] += s[axis] * 0.5f;
                pos.y += MUZZLE_Y_RAISE;   // 落点偏下的补偿：出膛点整体上抬
                gunMuzzleT.localPosition = pos;
                Debug.Log("[M82] 枪口点(含上抬) = " + pos.ToString("F3"));
                Debug.Log("[M82] Gun Muzzle 移到模型前端: " + pos.ToString("F3") + "（最长轴=" + axis + "，尺寸=" + s.ToString("F3") + "）");
            }

            // ---------- 资源 ----------
            var phys = ScriptableObject.CreateInstance<PhysAttribute>();
            phys.name = "PhysAttribute M82";
            SetPriv(phys, "mass", 0.5f);
            SaveAsset(phys, "PhysAttribute M82.asset");

            var colors = ScriptableObject.CreateInstance<ColorPresets>();
            colors.name = "ColorPresets M82";
            colors.colorMain = new Color(0.95f, 0.55f, 0.15f, 1f);
            colors.colorLight = new Color(1f, 0.87f, 0.45f, 1f);
            colors.colorDark = new Color(0.35f, 0.18f, 0.05f, 1f);
            SaveAsset(colors, "ColorPresets M82.asset");

            var val = ScriptableObject.CreateInstance<Value>();
            val.name = "Value M82";
            val.valueMin = 20000f;   // 商店 ×4 → 80000   // 商店 ×4 → 70000   // 商店 ×4 → 60000   // 商店价 ×4 → 50000
            val.valueMax = 22500f;   // 商店 ×4 → 90000   // 商店 ×4 → 90000   // 商店 ×4 → 80000   // 商店价 ×4 → 70000
            SaveAsset(val, "Value M82.asset");

            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "Item Gun M82";
            SetPriv(item, "itemAssetName", "Item Gun M82");
            SetPriv(item, "itemName", "M82");
            SetPriv(item, "itemType", 9);
            SetPriv(item, "emojiIcon", 23);
            SetPriv(item, "itemVolume", 1);
            SetPriv(item, "maxAmount", 2);
            SetPriv(item, "maxAmountInShop", 2);
            SetPriv(item, "maxPurchaseAmount", 2);
            SetPriv(item, "physicalItem", true);
            SetPriv(item, "value", val);
            SetPriv(item, "colorPreset", colors);
            SaveAsset(item, "Item M82.asset");
            try
            {
                var soItem = new SerializedObject(AssetDatabase.LoadAssetAtPath<Item>("Assets/M82MOD/Generated/Item M82.asset"));
                var pv = soItem.FindProperty("itemVolume");
                if (pv != null) { pv.intValue = 5; soItem.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(soItem.targetObject);
                var pv2 = soItem.FindProperty("itemSecretShopType");
                if (pv2 != null) { pv2.intValue = 0; soItem.ApplyModifiedPropertiesWithoutUndo(); }
                Debug.Log("[M82] Item.itemVolume=" + (pv != null ? pv.intValue.ToString() : "?"));
            }
            catch (Exception e) { Debug.LogWarning("[M82] Item 体积设置失败: " + e.Message); }

            // ---------- 接到 prefab ----------
            var attr = root.GetComponent<ItemAttributes>();
            if (attr == null) throw new Exception("克隆出的 prefab 上没有 ItemAttributes");
            attr.item = item;
            SetPriv(attr, "item", item);
            SetPriv(attr, "colorPreset", colors);
            EditorUtility.SetDirty(attr);

            var notVal = root.GetComponent<NotValuableObject>();
            if (notVal != null) { notVal.physAttributePreset = phys; EditorUtility.SetDirty(notVal); }

            var rb = root.GetComponent<Rigidbody>();
            if (rb != null) { rb.mass = 0.8f; EditorUtility.SetDirty(rb); }

            var battery = root.GetComponent<ItemBattery>();
            if (battery != null)
            {
                SetPriv(battery, "itemAttributes", attr);
                SetPriv(battery, "autoDrain", false);
                SetPriv(battery, "batteryBars", 5);        // 弹药格数：明确 30
                SetPriv(battery, "batteryLife", 100f);
                SetPriv(battery, "batteryDrainRate", 1f);
                EditorUtility.SetDirty(battery);
                Debug.Log("[M82] 电池：格数=30 电量=100");
            }

            var gun = root.GetComponent<ItemGun>();
            if (gun != null)
            {
                // 原版 Tranq 的子弹是"麻醉针"（有模型、慢速），换成我们自己的快速命中子弹
                var vanillaBullet = FindDeep(root.transform, "Bullet");
                if (vanillaBullet != null)
                {
                    UnityEngine.Object.DestroyImmediate(vanillaBullet.gameObject);
                    Debug.Log("[M82] 已删除原版麻醉针子弹模板");
                }
                var bulletPrefab = BuildBullet(mat, lineMat, partMat);
                Debug.Log("[M82] 已生成自己的子弹模板");

                // 自己的枪口火焰（原版麻醉枪的太小/不对）：三粒子 + Shoot Light，游戏按名字 Find
                var flashPrefab = BuildMuzzleFlash(partMat);
                Debug.Log("[M82] 已生成自己的枪口火焰");

                SetPriv(gun, "bulletPrefab", bulletPrefab);
                SetPriv(gun, "muzzleFlashPrefab", flashPrefab);
                SetPriv(gun, "grabVerticalOffset", -0.30f);   // 拿枪位置往下
            SetPriv(gun, "aimVerticalOffset", -7.0f);    // 瞄准时也更低一点
            SetPriv(gun, "gunRecoilForce", RECOIL);
                SetPriv(gun, "shootCooldown", COOLDOWN);
            SetPriv(gun, "gunRandomSpread", 0f);   // 散布归零 → 指哪打哪
            SetPriv(gun, "shootTime", 0.3f);   // 换弹时长 2.5 秒
                SetPriv(gun, "cameraShakeMultiplier", 2.0f);   // 屏幕晃动加大   // 屏幕晃动加大
                SetPriv(gun, "misfirePercentageChange", 0f);
                SetPriv(gun, "batteryDrain", BATTERY_DRAIN);
                SetPriv(gun, "batteryDrainFullBar", true);
                SetPriv(gun, "batteryDrainFullBars", 1);
                // 声音：用真实枪械录音（AR-15 单发裁剪版），空仓用咔哒声
                var shootClip = AssetDatabase.LoadAssetAtPath<AudioClip>(GEN_DIR + "/M82 Shoot.wav");
                if (shootClip == null) shootClip = AssetDatabase.LoadAssetAtPath<AudioClip>(GEN_DIR + "/M82 Shoot.wav");
                var emptyClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/M82MOD/Generated/M82 Empty.wav");
                if (shootClip != null)
                {
                    SetSound(gun, "soundShoot", shootClip, 1f, 0.35f, 2.5f);
                    // 游戏 Shoot 里会连续播 soundShoot 和 soundShootGlobal 两条 → 一条开枪两声。
                    // 把「全局」那条换成静音（音量 0），只保留本地那条。
                    SetSound(gun, "soundShootGlobal", emptyClip != null ? emptyClip : shootClip, 0f, 0.5f, 2.5f);
                    SetSound(gun, "soundHit", emptyClip, 0.5f);
                }
                if (emptyClip != null) SetSound(gun, "soundNoAmmoClick", emptyClip, 0.6f);
                EditorUtility.SetDirty(gun);
                Debug.Log("[M82] 枪参数：后坐力=" + RECOIL + " 每发耗电=" + BATTERY_DRAIN + "（子弹/弹道/粒子沿用原版，声音已换成自己的）");
            }

            // 枪本体不要伤害碰撞体（原版克隆来的 HurtCollider 会让撞到枪的玩家掉血/致死）
            foreach (var hcGun in root.GetComponentsInChildren<HurtCollider>(true))
            {
                if (hcGun == null) continue;
                SetPriv(hcGun, "playerLogic", false);
                SetPriv(hcGun, "playerDamage", 0f);
                SetPriv(hcGun, "playerKill", false);
                SetPriv(hcGun, "enemyLogic", false);
                SetPriv(hcGun, "enemyDamage", 0f);
                SetPriv(hcGun, "physLogic", false);
                hcGun.enabled = false;
                Debug.Log("[M82] 已禁用枪本体 HurtCollider: " + hcGun.gameObject.name);
            }

            // 体积：large_high（0=small 1=medium 2=large 3=large_wide 4=power_crystal 5=large_high ...）
            foreach (var iv in root.GetComponentsInChildren<ItemVolume>(true))
            {
                if (iv == null) continue;
                try
                {
                    var so = new SerializedObject(iv);
                    var prop = so.FindProperty("itemVolume");
                    if (prop != null) { prop.intValue = 5; so.ApplyModifiedPropertiesWithoutUndo(); }   // 5 = large_high
                    EditorUtility.SetDirty(iv);
                    Debug.Log("[M82] 体积设为 large_high(5): " + iv.gameObject.name + " 实际=" + (prop != null ? prop.intValue.ToString() : "?"));
                }
                catch (Exception e) { Debug.LogWarning("[M82] 体积设置失败: " + e.Message); }
            }
            SetPriv(item, "itemVolume", 5);

            var gunPrefab = SavePrefab(root, "Item Gun M82.prefab");

            // ---------- REPOLib: Mod + ItemContent ----------
            var mod = ScriptableObject.CreateInstance<REPOLib.Objects.Sdk.Mod>();
            mod.name = "Mod M82";
            SetPriv(mod, "_name", "M82");
            SetPriv(mod, "_author", "cn_xc");
            SetPriv(mod, "_version", "0.1.1");
            SetPriv(mod, "_description", "M82 rifle");
            SaveAsset(mod, "Mod M82.asset");

            var pfAttr = gunPrefab.GetComponent<ItemAttributes>();
            var content = ScriptableObject.CreateInstance<REPOLib.Objects.Sdk.ItemContent>();
            content.name = "M82 Content";
            SetPriv(content, "_prefab", pfAttr);
            try
            {
                var soC = new SerializedObject(content);
                var pC = soC.FindProperty("_prefab");
                if (pC != null) { pC.objectReferenceValue = pfAttr; soC.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(content);
                Debug.Log("[M82] Content._prefab = " + (pC != null && pC.objectReferenceValue != null ? "OK" : "NULL"));
            }
            catch (Exception e) { Debug.LogError("[M82] 绑定 prefab 失败: " + e); }
            SaveAsset(content, "M82 Content.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[M82] 物品生成完成");
            BuildBundle();
        }
        catch (Exception e)
        {
            Debug.LogError("[M82] 失败: " + e);
        }
    }

    static void BuildBundle()
    {
        try
        {
            // 内置 shader 必须进包，否则运行时会渲染成紫红色
            EnsureAlwaysIncludedShaders("Standard", "Sprites/Default", "Legacy Shaders/Particles/Alpha Blended",
                                        "TextMeshPro/Mobile/Distance Field", "TextMeshPro/Distance Field");

            var outDir = GEN_DIR + "/BundleOut";
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

            foreach (var g in AssetDatabase.FindAssets(string.Empty, new[] { GEN_DIR }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("BundleOut")) continue;
                var imp = AssetImporter.GetAtPath(p);
                if (imp != null && !(imp is MonoImporter)) imp.assetBundleName = BUNDLE;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

            var built = outDir + "/" + BUNDLE;
            if (!File.Exists(built)) { Debug.LogError("[M82] 打包失败：未生成 " + built); return; }

            var final = MOD_DIR + "/M82.repobundle";
            if (File.Exists(final)) File.Delete(final);
            File.Copy(built, final);
            Debug.Log("[M82] 打包完成: " + final);

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dstDir = Path.Combine(appData, "Thunderstore Mod Manager", "DataFolder", "REPO", "profiles", "Default", "BepInEx", "plugins", "cn_xc-M82");
                Directory.CreateDirectory(dstDir);
                File.Copy(final, Path.Combine(dstDir, "M82.repobundle"), true);
                Debug.Log("[M82] 已安装到: " + dstDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[M82] 自动安装失败: " + ex.Message);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[M82] 打包失败: " + e);
        }
    }

    [MenuItem("REPO/仅打包")]
    public static void BundleOnly() { BuildBundle(); }

    /// <summary>枪口火焰：游戏 ActivateAllEffects() 会在根下 Find("Particle Smoke"/"Particle Impact"/"Particle Sparks") 和 GetComponentInChildren&lt;Light&gt;()。</summary>
    static GameObject BuildMuzzleFlash(Material partMat)
    {
        var flash = new GameObject("MuzzleFlash");
        flash.AddComponent<ItemGunMuzzleFlash>();
        // 收紧：寿命 0.07~0.09s、速度 0.6~1.2、粒子更大更密、锥角 8° —— 看起来是一团枪口焰，不是散开的烟
        AddPs(flash, "Particle Smoke", new Color(0.75f, 0.72f, 0.68f, 0.35f), partMat, 0.09f, 0.7f, 0.20f, 6);
        AddPs(flash, "Particle Impact", new Color(1f, 0.55f, 0.12f, 0.95f), partMat, 0.08f, 1.1f, 0.28f, 14);
        AddPs(flash, "Particle Sparks", new Color(1f, 0.9f, 0.55f, 1f), partMat, 0.07f, 1.6f, 0.12f, 22);
        var lightGo = new GameObject("Shoot Light");
        lightGo.transform.SetParent(flash.transform, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 6f;
        l.intensity = 4f;
        l.color = new Color(1f, 0.8f, 0.45f);
        l.enabled = true;
        return SavePrefab(flash, "MuzzleFlash.prefab");
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>子弹模板：结构完全对齐 AK47（ItemGunBullet + Hurt Collider + Shoot Line + Hit Effect 下三粒子/Hit Light）。</summary>
    static GameObject BuildBullet(Material gunMat, Material lineMat, Material partMat)
    {
        var bullet = new GameObject("Bullet");
        var brb = bullet.AddComponent<Rigidbody>();   // 必须要有刚体，ItemGunBullet 靠它给速度
        brb.mass = 0.2f;
        brb.drag = 0f;
        brb.angularDrag = 0f;
        brb.useGravity = false;
        brb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "bullet";
        body.transform.SetParent(bullet.transform, false);
        body.transform.localScale = Vector3.one * 0.05f;
        var bc = body.GetComponent<SphereCollider>();
        if (bc != null) UnityEngine.Object.DestroyImmediate(bc);
        var mr = body.GetComponent<MeshRenderer>();
        if (mr != null && gunMat != null) mr.sharedMaterial = gunMat;
        if (mr != null) mr.enabled = false;   // 不显示球体，只保留 Shoot Line 弹道

        var hurtGo = new GameObject("Hurt Collider");
        hurtGo.transform.SetParent(bullet.transform, false);
        var hbox = hurtGo.AddComponent<BoxCollider>();
        hbox.size = Vector3.one * 0.15f;
        var hurt = hurtGo.AddComponent<HurtCollider>();
        SetupHurt(hurt);

        var lrGo = new GameObject("Shoot Line");
        lrGo.transform.SetParent(bullet.transform, false);
        var lr = lrGo.AddComponent<LineRenderer>();
        lr.positionCount = 4;
        lr.useWorldSpace = true;
        lr.startWidth = 0.03f;
        lr.endWidth = 0.03f;
        if (lineMat != null) lr.material = lineMat;
        lr.startColor = new Color(1f, 0.9f, 0.5f, 0.9f);
        lr.endColor = new Color(1f, 0.6f, 0.2f, 0.3f);
        lr.enabled = true;

        var hitEffect = new GameObject("Hit Effect");
        hitEffect.transform.SetParent(bullet.transform, false);
        AddPs(hitEffect, "Particle Sparks", new Color(1f, 0.85f, 0.4f, 1f), partMat);
        AddPs(hitEffect, "Particle Smoke", new Color(0.6f, 0.6f, 0.6f, 0.4f), partMat);
        AddPs(hitEffect, "Particle Impact", new Color(1f, 0.7f, 0.3f, 0.8f), partMat);
        var hitLightGo = new GameObject("Hit Light");
        hitLightGo.transform.SetParent(hitEffect.transform, false);
        var hl = hitLightGo.AddComponent<Light>();
        hl.type = LightType.Point; hl.range = 2f; hl.intensity = 2f;
        hl.color = new Color(1f, 0.8f, 0.4f);
        hl.enabled = true;

        var logic = bullet.AddComponent<ItemGunBullet>();
        SetPriv(logic, "hasHurtCollider", true);
        SetPriv(logic, "hurtCollider", hurt);
        SetPriv(logic, "hurtColliderTimer", 0.25f);
        SetPriv(logic, "hitGameObject", body);
        SetPriv(logic, "hitGameObjectDestroyTime", 2f);

        return SavePrefab(bullet, "Bullet.prefab");
    }

    static void AddPs(GameObject parent, string name, Color color, Material mat, float life = 0.25f, float speed = 3f, float size = 0.08f, int count = 8)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor = color;
        main.startLifetime = life;
        main.startSpeed = speed;
        main.startSize = size;
        main.duration = 0.12f;
        main.loop = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.01f;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var rend = go.GetComponent<ParticleSystemRenderer>();
        if (rend != null && mat != null) { rend.material = mat; rend.renderMode = ParticleSystemRenderMode.Billboard; }
        ps.Play();
    }

    static void SetupHurt(HurtCollider hurt)
    {
        SetPriv(hurt, "playerLogic", false);   // 不对玩家生效（防自杀）
        SetPriv(hurt, "playerDamage", 0);
        SetPriv(hurt, "playerKill", false);
        SetPriv(hurt, "playerHitForce", 10f);
        SetPriv(hurt, "playerDamageCooldown", 2f);
        SetPriv(hurt, "playerRayCast", true);
        SetPriv(hurt, "playerTumbleForce", 15f);
        SetPriv(hurt, "playerTumbleTorque", 20f);
        SetPriv(hurt, "playerTumbleTime", 2f);
        SetPriv(hurt, "physLogic", true);
        SetPriv(hurt, "physDestroy", false);
        SetPriv(hurt, "physImpact", 3);
        SetPriv(hurt, "physDamageCooldown", 2f);
        SetPriv(hurt, "physHitForce", 10f);
        SetPriv(hurt, "physHitTorque", 20f);
        SetPriv(hurt, "enemyLogic", true);
        SetPriv(hurt, "enemyDamage", 500);
        SetPriv(hurt, "enemyKill", false);
        SetPriv(hurt, "enemyStun", true);
        SetPriv(hurt, "enemyStunTime", 2.5f);
        SetPriv(hurt, "enemyStunType", 2);
        SetPriv(hurt, "enemyFreezeTime", 0.2f);
        SetPriv(hurt, "enemyImpact", 3);
        SetPriv(hurt, "enemyDamageCooldown", 2f);
        SetPriv(hurt, "enemyHitForce", 15f);
        SetPriv(hurt, "enemyHitTorque", 30f);
        SetPriv(hurt, "hitSpread", 10f);
        SetPriv(hurt, "timer", 0.2f);
    }

    static void EnsureAlwaysIncludedShaders(params string[] names)
    {
        try
        {
            var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (objs == null || objs.Length == 0) { Debug.LogWarning("[M82] 读不到 GraphicsSettings"); return; }
            var so = new SerializedObject(objs[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) { Debug.LogWarning("[M82] 找不到 m_AlwaysIncludedShaders"); return; }
            var have = new HashSet<string>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var sh = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (sh != null) have.Add(sh.name);
            }
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) { Debug.LogWarning("[M82] 找不到 shader: " + n); continue; }
                if (have.Contains(sh.name)) continue;
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                have.Add(sh.name);
                Debug.Log("[M82] 已加入 Always Included Shader: " + n);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
        catch (Exception e) { Debug.LogWarning("[M82] 处理 Always Included Shader 失败: " + e.Message); }
    }

    static void SetSound(UnityEngine.Object target, string field, AudioClip clip, float volume, float spatialBlend = -1f, float falloff = -1f)
    {
        try
        {
            var so = new SerializedObject(target);
            var arr = so.FindProperty(field + ".Sounds");
            if (arr != null && arr.isArray)
            {
                arr.arraySize = 1;
                arr.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            }
            var vol = so.FindProperty(field + ".Volume");
            if (vol != null) vol.floatValue = volume;
            // 关键：音高必须重置（克隆来的原版枪 Pitch 可能是 2，听起来完全不对）
            var pitchP = so.FindProperty(field + ".Pitch");
            if (pitchP != null) pitchP.floatValue = 1f;
            var pitchR = so.FindProperty(field + ".PitchRandom");
            if (pitchR != null) pitchR.floatValue = 0f;
            var volR = so.FindProperty(field + ".VolumeRandom");
            if (volR != null) volR.floatValue = 0f;
            if (spatialBlend >= 0f)
            {
                var sb = so.FindProperty(field + ".SpatialBlend");
                if (sb != null) sb.floatValue = spatialBlend;
            }
            if (falloff > 0f)
            {
                var fo = so.FindProperty(field + ".FalloffMultiplier");
                if (fo != null) fo.floatValue = falloff;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        catch { }
    }

    static void SaveAsset(UnityEngine.Object o, string file)
    {
        var path = GEN_DIR + "/" + file;
        AssetDatabase.CreateAsset(o, path);
        EditorUtility.SetDirty(o);
    }

    static GameObject SavePrefab(GameObject go, string file)
    {
        var path = GEN_DIR + "/" + file;
        var p = PrefabUtility.SaveAsPrefabAsset(go, path);
        UnityEngine.Object.DestroyImmediate(go);
        return p;
    }

    static void SetPriv(UnityEngine.Object target, string field, object value)
    {
        try
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            if (value is bool b) p.boolValue = b;
            else if (value is int i) p.intValue = i;
            else if (value is float f) p.floatValue = f;
            else if (value is string s) p.stringValue = s;
            else if (value is UnityEngine.Object o) p.objectReferenceValue = o;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        catch { }
    }
}
