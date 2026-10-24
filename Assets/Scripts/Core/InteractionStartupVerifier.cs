using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;

/// <summary>
/// 交互自检 —— 启动后扫描场景里所有可交互物体，把【注册失败】的补注册一遍。
///
/// 为什么需要这个：
///   XRInteractionManager 内部是一张 Collider → Interactable 的字典。
///   Interactable 是在 OnEnable 里注册的，注册时会读它自己的 colliders 列表。
///
///   本工程的道具（夹板/冰袋/绷带/保温毯/隔热垫/警示标志/手机）
///   碰撞体都是运行时用 AddComponent 加上的，而关卡根物体是
///   【选关之后才 SetActive(true)】的。同一帧批量启用物体时：
///     · 有的 Interactable 在碰撞体生成之前就 OnEnable 了
///       → 注册时 colliders 是空的 → 字典里没有它的映射
///       → 射线打上去拿不到 Interactable → 不高亮、选不中
///     · 而且它只注册一次，之后补了碰撞体也不会重新注册
///   注册顺序在同一帧内不稳定，所以【每局失效的道具都不一样】。
///
/// 用法：
///   挂在 GameManager 上（或任意常驻物体）。不用配置。
/// </summary>
public class InteractionStartupVerifier : MonoBehaviour
{
    [Header("时机")]
    [Tooltip("第一次检查前等待多久（等关卡内容被激活）")]
    public float firstRunDelay = 0.3f;

    [Tooltip("检查几轮（每轮重新扫描一次）")]
    public int passes = 4;

    [Tooltip("每轮之间等待多久")]
    public float passInterval = 0.5f;

    [Header("调试")]
    [Tooltip("打印详细日志（每件道具的结果）。排查时才开")]
    public bool logVerbose = false;

    [Tooltip("只在真的补注册了东西时才打日志")]
    public bool logRepairs = true;

    private void Start()
    {
        StartCoroutine(VerifyRoutine());
    }

    private System.Collections.IEnumerator VerifyRoutine()
    {
        yield return new WaitForSeconds(firstRunDelay);

        int totalRepaired = 0;

        for (int pass = 0; pass < Mathf.Max(1, passes); pass++)
        {
            int repaired = VerifyOnce(pass);

            if (repaired > 0)
            {
                totalRepaired += repaired;
                if (logRepairs)
                    Debug.Log($"[交互自检] 第 {pass + 1} 轮：补注册了 {repaired} 个可交互物体");
            }

            if (pass < passes - 1)
                yield return new WaitForSeconds(passInterval);
        }

        if (totalRepaired == 0)
        {
            if (logRepairs)
                Debug.Log("[交互自检] 全部可交互物体注册正常，无需修复");
        }
        else
        {
            Debug.Log($"[交互自检] 共补注册 {totalRepaired} 次");
        }
    }

    /// <summary>扫描一遍，返回补注册的个数</summary>
    private int VerifyOnce(int pass)
    {
        var manager = FindAnyObjectByType<XRInteractionManager>();
        if (manager == null)
        {
            Debug.LogWarning("[交互自检] 场景里找不到 XRInteractionManager");
            return 0;
        }

        // 把所有可交互物体都找出来（包含未激活的，等它们激活后再查）
        var all = new List<XRBaseInteractable>();
        all.AddRange(FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include));
        all.AddRange(FindObjectsByType<XRSimpleInteractable>(FindObjectsInactive.Include));

        int repaired = 0;

        foreach (var interactable in all)
        {
            if (interactable == null) continue;

            // 没激活的跳过（等下一轮）
            if (!interactable.isActiveAndEnabled) continue;

            // ---------- 碰撞体齐了吗 ----------
            var colliders = interactable.colliders;

            if (colliders == null || colliders.Count == 0)
            {
                // 自动补：用本物体和子物体上的所有碰撞体
                var found = interactable.GetComponentsInChildren<Collider>(true);

                if (found == null || found.Length == 0)
                {
                    if (logVerbose)
                        Debug.LogWarning($"[交互自检] 「{interactable.name}」没有碰撞体，" +
                                         $"无法交互（射线打不到它）");
                    continue;
                }

                foreach (var c in found)
                {
                    if (c == null) continue;
                    colliders.Add(c);
                }

                if (logVerbose)
                    Debug.Log($"[交互自检] 「{interactable.name}」补上了 {found.Length} 个碰撞体引用");
            }

            // ---------- 注册状态对不对 ----------
            // ⚠️ 必须显式转成 IXRInteractable —— XRInteractionManager 上有
            //    已过时的 XRBaseInteractable 重载，不转换会命中它并报 CS0619。
            var ixr = (IXRInteractable)interactable;

            bool registered = manager.IsRegistered(ixr);

            // 已注册也要重来一次：可能是"注册过但当时 colliders 是空的"
            manager.UnregisterInteractable(ixr);
            manager.RegisterInteractable(ixr);

            repaired++;

            if (logVerbose)
                Debug.Log($"[交互自检] 「{interactable.name}」重新注册" +
                          $"（之前 registered = {registered}，" +
                          $"碰撞体 = {colliders.Count} 个）");
        }

        return repaired;
    }
}
