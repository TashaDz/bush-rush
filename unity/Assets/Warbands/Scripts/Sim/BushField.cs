using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    /// Гекс поля кустов (ветка bush-field, автор 10.09; с 11.09 гексы): X — столбец слева, Y — строка сверху (0 — тыл врага, Rows-1 — тыл игрока); нечётные строки сдвинуты вправо на полгекса.
    [Serializable]
    public struct Cell : IEquatable<Cell>
    {
        public int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell o) => X == o.X && Y == o.Y;
        public override bool Equals(object o) => o is Cell c && Equals(c);
        public override int GetHashCode() => X * 131 + Y;
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => $"({X},{Y})";
    }

    /// Поле 6×10 гексов (pointy-top, odd-r; автор 11.09 — гексы вместо клеток, потом 6×10 вместо 7×12 ради крупных гексов): строки 0–1 — зона врага (тыл, фронт), 8–9 — зона игрока (фронт, тыл). Кусты везде (автор 10.09), кроме гексов под отрядами. Колонки расстановки — по чётности строки (1,3,5 / 0,2,4).
    /// Ходят по шести соседям; дальность стрельбы — по прямой между центрами в ширинах гекса.
    public static class BushGrid
    {
        public const int Cols = 6, Rows = 10, BushTop = 0, BushBottom = Rows - 1;   // автор 10.09: кусты на всём поле; 11.09: 6×10 вместо 7×12 — гексы крупнее на ~17 % («слишком кучно»)
        // колонки расстановки трёх слотов: в чётных строках 1, 3, 5, в нечётных (сдвинутых на полгекса вправо) 0, 2, 4 — армия по центру поля
        public static readonly int[] SlotColsEven = { 1, 3, 5 }, SlotColsOdd = { 0, 2, 4 };

        public static bool Inside(Cell c) => c.X >= 0 && c.X < Cols && c.Y >= 0 && c.Y < Rows;
        public static bool IsBushZone(Cell c) => c.Y >= BushTop && c.Y <= BushBottom;
        public static int Index(Cell c) => c.Y * Cols + c.X;

        // гексы pointy-top, offset odd-r (автор 11.09: гекс лучше под палец): нечётные строки сдвинуты на полгекса вправо, у гекса 6 соседей
        static readonly int[] EvenDx = { -1, 1, -1, 0, -1, 0 }, EvenDy = { 0, 0, -1, -1, 1, 1 };
        static readonly int[] OddDx = { -1, 1, 0, 1, 0, 1 }, OddDy = { 0, 0, -1, -1, 1, 1 };
        /// Смещение k-го соседа (0 W, 1 E, 2 NW, 3 NE, 4 SW, 5 SE) для строки данной чётности.
        public static void NeighborDelta(int y, int k, out int dx, out int dy) { bool odd = (y & 1) == 1; dx = odd ? OddDx[k] : EvenDx[k]; dy = odd ? OddDy[k] : EvenDy[k]; }
        public static IEnumerable<Cell> Neighbors(Cell c)
        {
            for (int i = 0; i < 6; i++) { NeighborDelta(c.Y, i, out int dx, out int dy); var n = new Cell(c.X + dx, c.Y + dy); if (Inside(n)) yield return n; }
        }
        /// Кубические координаты (q, r) из offset odd-r.
        public static void Cube(Cell c, out int q, out int r) { q = c.X - (c.Y - (c.Y & 1)) / 2; r = c.Y; }
        public static Cell FromCube(int q, int r) => new Cell(q + (r - (r & 1)) / 2, r);
        /// Шагов между гексами.
        public static int Steps(Cell a, Cell b) { Cube(a, out int aq, out int ar); Cube(b, out int bq, out int br); int dq = aq - bq, dr = ar - br; return Math.Max(Math.Abs(dq), Math.Max(Math.Abs(dr), Math.Abs(dq + dr))); }
        public static bool Adjacent(Cell a, Cell b) => Steps(a, b) == 1;
        public const float RowPitch = 0.8660254f;   // шаг строк в ширинах гекса (pointy-top: 0.75 высоты)
        /// Центр гекса в ширинах гекса: x — столбец плюс полгекса в нечётных строках, y — строка × 0.866.
        public static void Center(Cell c, out float x, out float y) { x = c.X + ((c.Y & 1) == 1 ? 0.5f : 0f); y = c.Y * RowPitch; }
        /// Расстояние между центрами по прямой, в ширинах гекса (соседи — ровно 1): физический рейндж стрелков.
        public static float Dist(Cell a, Cell b) { Center(a, out float ax, out float ay); Center(b, out float bx, out float by); float dx = ax - bx, dy = ay - by; return (float)Math.Sqrt(dx * dx + dy * dy); }
        /// Гексы на отрезке от a до b (без a, с b): линия по кубическим координатам с округлением — для непрерывного росчерка пальцем.
        public static List<Cell> Line(Cell a, Cell b)
        {
            var l = new List<Cell>(); int n = Steps(a, b); if (n == 0) return l;
            Cube(a, out int aq, out int ar); Cube(b, out int bq, out int br); float asx = -aq - ar, bsx = -bq - br;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n; float q = aq + (bq - aq) * t + 1e-4f, r = ar + (br - ar) * t + 1e-4f, sx = asx + (bsx - asx) * t - 2e-4f;
                int rq = (int)Math.Round(q), rr = (int)Math.Round(r), rs = (int)Math.Round(sx);
                float dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - sx);
                if (dq > dr && dq > ds) rq = -rr - rs; else if (dr > ds) rr = -rq - rs;
                var c = FromCube(rq, rr); if (Inside(c) && (l.Count == 0 || l[l.Count - 1] != c)) l.Add(c);
            }
            return l;
        }

        /// Домашний гекс по расстановке: сторона 0 (игрок) снизу — фронт строка Rows-2, тыл Rows-1; сторона 1 сверху — фронт 1, тыл 0.
        public static Cell Home(int side, Row row, int slot)
        {
            // Bush Rush: крайние строки — героев, отряды на строку ближе к центру: игрок — фронт Rows-3, тыл Rows-2; враг — фронт 2, тыл 1
            int y = side == 0 ? (row == Row.Front ? Rows - 3 : Rows - 2) : (row == Row.Front ? 2 : 1);
            int col = ((y & 1) == 0 ? SlotColsEven : SlotColsOdd)[Math.Max(0, Math.Min(2, slot))];
            return new Cell(col, y);
        }

        /// Гекс героя (Bush Rush, автор 14.09): игрок снизу (2, Rows-1) на поле; враг — за верхним краем, на «площадке» (2, -1) (автор 14.09: «переставь красного героя сюда»).
        /// Гекс вне сетки: соседство/дистанции считаются как обычно (кубовые координаты), бойцы туда не ступают (Passable требует Inside), бьют с (2,0) и (3,0).
        public static Cell HeroCell(int side) => side == 0 ? new Cell(Cols / 2 - 1, Rows - 1) : new Cell(Cols / 2 - 1, -1);

        public static string Encode(IList<Cell> cells) { if (cells == null) return ""; var sb = new System.Text.StringBuilder(); for (int i = 0; i < cells.Count; i++) { if (i > 0) sb.Append(';'); sb.Append(cells[i].X).Append(',').Append(cells[i].Y); } return sb.ToString(); }
        public static List<Cell> Decode(string s)
        {
            var l = new List<Cell>(); if (string.IsNullOrEmpty(s)) return l;
            foreach (var p in s.Split(';')) { var xy = p.Split(','); if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) l.Add(new Cell(x, y)); }
            return l;
        }
    }

    /// Скорость (клеток за ход) и рейндж (клеток по прямой) — предложение под ветку bush-field; числа карт не трогаем.
    /// Ближники вдвое быстрее дальников (автор), ассасины ещё быстрее.
    public static class BushStats
    {
        public static int Speed(SquadId id)
        {
            switch (id)
            {
                case SquadId.VeilKnives: return 6;   // Bush Rush (14.09): поле 6×10, между фронтами 4 строки — скорости 5/6/3 (в Warbands были 7/8/4); число предложено, не из карт
                case SquadId.IronWardens: case SquadId.BloodboundReavers: case SquadId.BoneCohort: case SquadId.Graveguard: case SquadId.SpellEaters: return 5;
                default: return 3;
            }
        }
        public static float Range(SquadId id)
        {
            switch (id)
            {
                case SquadId.RoyalArbalists: return 4f;
                case SquadId.ArmorbreakGunners: case SquadId.PlagueCabal: case SquadId.DawnClerics: case SquadId.Bonecallers: case SquadId.SpiritWeavers: return 3f;
                default: return 1f;   // ближники: соседняя клетка
            }
        }
        public static bool IsMelee(SquadDef d) => d.Reach != Reach.Ranged;
    }

    public enum BonusKind : byte { None, Double, Heal }   // ×2 — удвоить бойцов, +HP — вылечить bushHealBonus

    /// Кусты поля: true — куст (стена), false — расчищено. Расчищенное открыто обеим сторонам и отрастает назад через ход (автор 10.09):
    /// ClearedTurn — номер хода расчистки, куст возвращается в начале хода ClearedTurn + bushRegrowTurns, если клетка не занята.
    public sealed class BushState
    {
        public bool[] Bush = new bool[BushGrid.Cols * BushGrid.Rows];
        public int[] ClearedTurn = new int[BushGrid.Cols * BushGrid.Rows];
        public bool[] Obstacle = new bool[BushGrid.Cols * BushGrid.Rows];      // препятствия (автор 10.09): не расчистить, не пройти
        public BonusKind[] Bonus = new BonusKind[BushGrid.Cols * BushGrid.Rows];
        public static BushState Fresh()
        {
            var b = new BushState();
            for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++) { var c = new Cell(x, y); b.Bush[BushGrid.Index(c)] = BushGrid.IsBushZone(c); }
            return b;
        }
        public bool IsBush(Cell c) => BushGrid.Inside(c) && Bush[BushGrid.Index(c)] && !Obstacle[BushGrid.Index(c)];
        public bool IsObstacle(Cell c) => BushGrid.Inside(c) && Obstacle[BushGrid.Index(c)];
        public void SetObstacle(Cell c) { if (BushGrid.Inside(c)) { Obstacle[BushGrid.Index(c)] = true; Bush[BushGrid.Index(c)] = false; } }
        public BonusKind BonusAt(Cell c) => BushGrid.Inside(c) ? Bonus[BushGrid.Index(c)] : BonusKind.None;
        public void SetBonus(Cell c, BonusKind k) { if (BushGrid.Inside(c)) Bonus[BushGrid.Index(c)] = k; }
        public int BonusCount { get { int n = 0; foreach (var k in Bonus) if (k != BonusKind.None) n++; return n; } }
        public void Clear(Cell c, int turn = 0) { if (BushGrid.Inside(c)) { Bush[BushGrid.Index(c)] = false; ClearedTurn[BushGrid.Index(c)] = turn; } }
        /// Вернуть куст (только в зоне кустов).
        public void Regrow(Cell c) { if (BushGrid.IsBushZone(c) && !Obstacle[BushGrid.Index(c)]) Bush[BushGrid.Index(c)] = true; }
        /// Расчищенные клетки зоны кустов, чей срок вышел к ходу turn.
        public List<Cell> Due(int turn, int regrowTurns)
        {
            var l = new List<Cell>();
            for (int y = BushGrid.BushTop; y <= BushGrid.BushBottom; y++) for (int x = 0; x < BushGrid.Cols; x++) { var c = new Cell(x, y); int i = BushGrid.Index(c); if (!Bush[i] && !Obstacle[i] && turn - ClearedTurn[i] >= regrowTurns) l.Add(c); }
            return l;
        }
        public int Count { get { int n = 0; foreach (var b in Bush) if (b) n++; return n; } }
        public BushState Clone() => new BushState { Bush = (bool[])Bush.Clone(), ClearedTurn = (int[])ClearedTurn.Clone(), Obstacle = (bool[])Obstacle.Clone(), Bonus = (BonusKind[])Bonus.Clone() };
    }
}
