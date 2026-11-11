using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// 玩家对「大石头」的粗略 XZ 碰撞。
    ///
    /// 数据源：DinosaurInteractionSettings.solidObstacles（场景烘焙好的世界空间 Bounds）。
    /// 只挡高度 ≥ MinBlockingHeight 的实体；矮石、倒木、树桩视为可跨过。
    /// 树干不在这里处理——由 TerrainTreeObstacleRegistry.SlideMove 负责。
    ///
    /// 盒子采用「外扩玩家半径 + 最小穿透轴推出」的近似：正面撞盒面会停住，
    /// 斜向会沿面滑行；角部比真圆保守约 (√2-1)·radius，属于粗略碰撞的预期取舍。
    /// </summary>
    public static class PlayerSolidObstacleSolver
    {
        /// <summary>挡人实体最小高度（米）。当前场景矮道具最高 0.7975m、大石最矮 1.565m，1.0 落在空档中间。</summary>
        public const float MinBlockingHeight = 1f;

        /// <summary>推出后沿轴向多留的间隙（米），避免浮点误差让下一帧仍判重叠。</summary>
        private const float PushEpsilon = 1e-3f;

        /// <summary>连续推出轮数：同一点被两个盒子覆盖时，第二轮收尾。</summary>
        private const int PushIterations = 2;

        /// <summary>把烘焙 Bounds 过滤并投影成 XZ 矩形（Rect.x = 世界 X，Rect.y = 世界 Z）。</summary>
        public static Rect[] BuildBlockingXZ(Bounds[] solids)
        {
            if (solids == null || solids.Length == 0)
                return Array.Empty<Rect>();

            var blocks = new List<Rect>(solids.Length);
            for (int i = 0; i < solids.Length; i++)
            {
                Bounds bounds = solids[i];
                if (bounds.size.y < MinBlockingHeight)
                    continue;
                blocks.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
            }
            return blocks.ToArray();
        }

        /// <summary>点是否与任意外扩 radius 的挡人矩形重叠（严格 &lt;，与树干判定一致）。</summary>
        public static bool Overlaps(Vector2 xz, float radius, Rect[] blocks)
        {
            if (blocks == null)
                return false;
            for (int i = 0; i < blocks.Length; i++)
            {
                Rect block = blocks[i];
                if (xz.x > block.xMin - radius && xz.x < block.xMax + radius
                    && xz.y > block.yMin - radius && xz.y < block.yMax + radius)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 把点推出所有重叠的挡人矩形。每轮取最小穿透轴，轴优先级固定为左→右→下→上，
        /// 避免贴角时两帧来回切换推出面造成抖动。
        /// </summary>
        public static Vector2 PushOut(Vector2 xz, float radius, Rect[] blocks)
        {
            if (blocks == null || blocks.Length == 0)
                return xz;

            for (int pass = 0; pass < PushIterations; pass++)
            {
                bool pushed = false;
                for (int i = 0; i < blocks.Length; i++)
                {
                    Rect block = blocks[i];
                    float left = xz.x - (block.xMin - radius);
                    float right = (block.xMax + radius) - xz.x;
                    float down = xz.y - (block.yMin - radius);
                    float up = (block.yMax + radius) - xz.y;
                    if (left <= 0f || right <= 0f || down <= 0f || up <= 0f)
                        continue;

                    float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
                    if (nearest == left) xz.x = block.xMin - radius - PushEpsilon;
                    else if (nearest == right) xz.x = block.xMax + radius + PushEpsilon;
                    else if (nearest == down) xz.y = block.yMin - radius - PushEpsilon;
                    else xz.y = block.yMax + radius + PushEpsilon;
                    pushed = true;
                }
                if (!pushed)
                    break;
            }
            return xz;
        }
    }
}
