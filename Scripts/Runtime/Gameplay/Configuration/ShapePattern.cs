using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    public static class ShapePattern
    {
        // 原点在左上角，■/# 占格，□/. 空格；任务定义负责归一化与旋转。
        public static Cell[] Parse(string[] rows)
        {
            if (rows == null || rows.Length == 0 || string.IsNullOrEmpty(rows[0]))
                throw new ArgumentException("方块至少需要一行，使用 ■/□ 或 #/. 绘制。");
            int width = rows[0].Length; var cells = new HashSet<Cell>();
            for (int y = 0; y < rows.Length; y++)
            {
                if (rows[y] == null || rows[y].Length != width || rows[y].Any(c => !"■□#.".Contains(c)))
                    throw new ArgumentException("方块每行宽度必须相同，且只能使用 ■/□ 或 #/.。");
                for (int x = 0; x < width; x++) if (rows[y][x] == '■' || rows[y][x] == '#') cells.Add(new Cell(x, y));
            }
            if (cells.Count == 0) throw new ArgumentException("方块至少需要一个占格。");
            var seen = new HashSet<Cell>(); var pending = new Queue<Cell>(); pending.Enqueue(cells.First());
            var directions = new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) };
            while (pending.Count > 0)
            {
                var cell = pending.Dequeue(); if (!seen.Add(cell)) continue;
                foreach (var direction in directions) if (cells.Contains(cell + direction) && !seen.Contains(cell + direction)) pending.Enqueue(cell + direction);
            }
            if (seen.Count != cells.Count) throw new ArgumentException("方块占格必须通过上下左右相连，不能仅对角连接。");
            return cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        }
    }
}
