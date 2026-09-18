using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// MemoryCards3D 全流程冒烟测试（Unity Test Framework / PlayMode）：
/// 在真实播放模式里模拟完整对局——
///   玩家1故意翻错(翻3) → 断言换人/归零/全部盖回
///   玩家2按 1→9 连翻 → 断言 GameOver + 胜利面板可见
///   再来一局 → 断言完全重置
/// 命令行运行（Test Framework 自己管理 PlayMode 生命周期和退出码）：
///   Tuanjie.exe -batchmode -projectPath <项目> -runTests -testPlatform PlayMode
///              -testResults <结果.xml> -logFile <日志>
public class MemoryCards3DSmokeTests
{
    private GameManager _gm;

    [UnityTest]
    public IEnumerator FullGameFlow_Smoke()
    {
        Debug.Log("MC3D_SMOKE_START");

        // 主场景已被 SceneBuilder 登记进 Build Settings（场景0），按名字异步加载
        var op = SceneManager.LoadSceneAsync("Main");
        float loadT = 0f;
        while (!op.isDone)
        {
            loadT += Time.deltaTime;
            if (loadT > 30f) Assert.Fail("scene load timeout");
            yield return null;
        }

        float gmT = 0f;
        while ((_gm = Object.FindObjectOfType<GameManager>()) == null)
        {
            gmT += Time.deltaTime;
            if (gmT > 5f) Assert.Fail("GameManager not found after scene load");
            yield return null;
        }

        // ---- 初始状态断言 ----
        Assert.AreEqual(GameManager.GameState.AwaitInput, _gm.State, "initial state");
        Assert.AreEqual(1, _gm.CurrentPlayer, "initial player");
        Assert.AreEqual(1, _gm.ExpectedNumber, "initial expected");
        yield return new WaitForSeconds(0.5f); // 等UI刷首屏

        // ---- 阶段A：玩家1故意翻错（该找1，偏翻3）----
        Debug.Log("MC3D_SMOKE: P1 wrong flip (3)");
        _gm.HandleCardClicked(FindCard(3));
        yield return WaitFor(() => _gm.State == GameManager.GameState.AwaitInput, 15f, "P1 wrong flip not resolved");
        Assert.AreEqual(2, _gm.CurrentPlayer, "should switch to player 2");
        Assert.AreEqual(1, _gm.ExpectedNumber, "expected should reset to 1");
        Assert.AreEqual(0, _gm.CorrectCount, "count should reset to 0");
        foreach (var card in Object.FindObjectsOfType<Card>())
            Assert.IsFalse(card.IsFaceUp, "all cards face down after wrong flip");

        // ---- 阶段B：玩家2按 1→9 连翻获胜 ----
        for (int n = 1; n <= 9; n++)
        {
            Debug.Log("MC3D_SMOKE: P2 flip " + n);
            var card = FindCard(n);
            Assert.IsNotNull(card, "card " + n + " exists");
            Assert.IsFalse(card.IsFaceUp, "card " + n + " starts face down");
            _gm.HandleCardClicked(card);
            var target = n == 9 ? GameManager.GameState.GameOver : GameManager.GameState.AwaitInput;
            yield return WaitFor(() => _gm.State == target, 10f, "flip " + n + " not resolved");
            Assert.AreEqual(n, _gm.CorrectCount, "correct count after flipping " + n);
        }
        Assert.AreEqual(GameManager.GameState.GameOver, _gm.State, "game over reached");
        Assert.AreEqual(10, _gm.ExpectedNumber, "expected should be 10 after full run");

        // ---- 阶段C：胜利面板 + 再来一局完全重置 ----
        var ui = Object.FindObjectOfType<UIManager>();
        Assert.IsNotNull(ui, "UIManager exists");
        Assert.IsTrue(ui.IsVictoryVisible, "victory panel visible on win");

        Debug.Log("MC3D_SMOKE: restart game");
        _gm.RestartGame();
        yield return null; // 等一帧让重置生效
        Assert.AreEqual(GameManager.GameState.AwaitInput, _gm.State, "restarted to AwaitInput");
        Assert.AreEqual(1, _gm.CurrentPlayer, "restart back to player 1");
        Assert.AreEqual(1, _gm.ExpectedNumber, "restart expected 1");
        Assert.AreEqual(0, _gm.CorrectCount, "restart count 0");
        Assert.IsFalse(ui.IsVictoryVisible, "victory panel hidden after restart");
        foreach (var card in Object.FindObjectsOfType<Card>())
            Assert.IsFalse(card.IsFaceUp, "cards face down after restart");

        Debug.Log("MC3D_SMOKE_PASS");
    }

    /// 轮询等待条件成立，超时直接判失败（Test Framework 会捕获并终止本测试）
    private IEnumerator WaitFor(System.Func<bool> cond, float timeout, string what)
    {
        float t = 0f;
        while (!cond())
        {
            t += Time.deltaTime;
            if (t > timeout) Assert.Fail(what + " (timeout, state=" + _gm.State + ")");
            yield return null;
        }
    }

    private Card FindCard(int value)
    {
        foreach (var c in Object.FindObjectsOfType<Card>())
            if (c.Value == value) return c;
        return null;
    }
}
