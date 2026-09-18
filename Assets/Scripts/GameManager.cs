using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// 游戏总控（状态机 + 裁判）：
/// AwaitInput(等点击) → Flipping(翻牌动画中) → 判定 →
///   翻对：回 AwaitInput 继续连翻
///   翻错：Resolving(本轮全盖回) → 换人 → AwaitInput
/// 1→9 全按序翻完 → GameOver（弹胜利画面）
public class GameManager : MonoBehaviour
{
    public enum GameState { AwaitInput, Flipping, Resolving, GameOver }

    [Header("运行状态（Inspector 里可实时观察，方便调试）")]
    public GameState State = GameState.AwaitInput;
    public int CurrentPlayer = 1;   // 当前玩家（玩家1先手）
    public int ExpectedNumber = 1;  // 本回合“下一个要翻”的数字（严格 1→9 递增）
    public int CorrectCount = 0;    // 本回合已连续翻对的数量

    public const int TotalCards = 9; // 总卡数 = 胜利所需连对数

    private Card[] _cards;   // 场上所有卡片
    private readonly List<Card> _openedThisTurn = new List<Card>(); // 本回合已翻开的卡
    private UIManager _ui;

    void Awake()
    {
        _cards = FindObjectsOfType<Card>();
        _ui = FindObjectOfType<UIManager>();
    }

    void Start()
    {
        // 开局先刷一次 UI：玩家1回合 / 请找：1 / 连对：0/9
        _ui?.RefreshAll(this);
    }

    /// Card 点击的转发入口：只在“等输入”状态接受点击
    public void HandleCardClicked(Card card)
    {
        if (State != GameState.AwaitInput) return; // 动画中/结算中/已分胜负：一律不收
        if (card == null || card.IsFaceUp) return; // 已翻开的卡不能点

        State = GameState.Flipping;
        card.FlipUp(); // 动画播完后由 Card 回调 OnCardFlipFinished 做判定
    }

    /// 翻牌动画结束后的判定回调（由 Card 调用）
    public void OnCardFlipFinished(Card card)
    {
        if (State != GameState.Flipping) return; // 防御非预期时机的回调

        if (card.Value == ExpectedNumber)
        {
            // ---- 翻对 ----
            _openedThisTurn.Add(card);
            CorrectCount++;
            ExpectedNumber++;

            if (ExpectedNumber > TotalCards)
            {
                // 1→9 全部按顺序翻完：当前玩家获胜！
                State = GameState.GameOver;
                _ui?.ShowVictory(CurrentPlayer, this);
                return;
            }

            State = GameState.AwaitInput; // 允许继续翻下一张
            _ui?.RefreshAll(this);
        }
        else
        {
            // ---- 翻错：本轮翻开的全部盖回（含刚翻错的这张），然后换人 ----
            StartCoroutine(ResolveWrongFlip(card));
        }
    }

    /// 翻错结算协程：逐张盖回（张与张之间稍错开，动画更有节奏）→ 换人 → 重置
    private IEnumerator ResolveWrongFlip(Card wrongCard)
    {
        State = GameState.Resolving;

        _openedThisTurn.Add(wrongCard); // 翻错的这张也属于“本轮已翻开”

        foreach (Card c in _openedThisTurn)
        {
            c.FlipDown();
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(0.45f); // 等最后一张的翻转动画播完

        // 换人 + 本回合数据归零
        _openedThisTurn.Clear();
        CurrentPlayer = CurrentPlayer == 1 ? 2 : 1;
        ExpectedNumber = 1;
        CorrectCount = 0;

        State = GameState.AwaitInput;
        _ui?.RefreshAll(this);
    }

    /// “再来一局”按钮回调：盖回全部卡 + 重新洗牌 + 数据复位
    public void RestartGame()
    {
        if (State != GameState.GameOver) return; // 只有分出胜负后才能重开

        foreach (Card c in _cards)
            c.SetFaceDownImmediate();

        ShuffleCardPositions();

        CurrentPlayer = 1;
        ExpectedNumber = 1;
        CorrectCount = 0;
        _openedThisTurn.Clear();
        State = GameState.AwaitInput;

        _ui?.HideVictory();
        _ui?.RefreshAll(this);
    }

    /// Fisher-Yates 洗牌：把9张卡在3x3阵列里的“家位置”随机重新分配
    private void ShuffleCardPositions()
    {
        var slots = new List<Vector3>(_cards.Length);
        foreach (Card c in _cards) slots.Add(c.GetHomePosition());

        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }
        for (int i = 0; i < _cards.Length; i++)
            _cards[i].SetHomePosition(slots[i]);
    }
}
