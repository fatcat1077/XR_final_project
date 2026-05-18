using UnityEngine;
using UnityEngine.UI;                  // 如果你使用傳統 UI Text
using TMPro;                           // 如果你使用 TextMeshPro (推薦)
using UnityEngine.Windows.Speech;      // 必須引入微軟語音命名空間

public class SpeechTest : MonoBehaviour
{
    // 拖入你的 UI 文字元件，用來顯示即時字幕
    [SerializeField] private TMP_Text subtitleText; 

    private DictationRecognizer dictationRecognizer;

    void Start()
    {
        // 初始化聽寫辨識器
        dictationRecognizer = new DictationRecognizer();

        // 【核心】當你正在說話，系統還在「即時猜測」時，會一直觸發這個事件
        dictationRecognizer.DictationHypothesis += OnDictationHypothesis;

        // 當你說完話停頓，系統「確定」最終句子的內容時觸發
        dictationRecognizer.DictationResult += OnDictationResult;

        // 語音辨識結束（例如玩家太久沒說話自動超時）
        dictationRecognizer.DictationComplete += OnDictationComplete;

        // 發生錯誤時觸發
        dictationRecognizer.DictationError += OnDictationError;

        // 啟動麥克風開始辨識
        dictationRecognizer.Start();
        
        if (subtitleText != null)
        {
            subtitleText.text = "請開始說話...";
        }
    }

    // 1. 這裡負責做到「講到哪、辨識到哪」的動態字幕效果
    private void OnDictationHypothesis(string text)
    {
        if (subtitleText != null)
        {
            // 當前正在辨識中的字，會隨著你繼續講話而即時更新、修正
            subtitleText.text = text + "..."; 
        }
    }

    // 2. 當講完一句話停頓後，系統吐出最終確定的文字
    private void OnDictationResult(string text, ConfidenceLevel confidence)
    {
        if (subtitleText != null)
        {
            // 將最終確定的文字更新上去（這時候尾巴不會有 "..." 了）
            subtitleText.text = text; 
        }
    }

    private void OnDictationComplete(DictationCompletionCause cause)
    {
        // 如果是因為太久沒說話而自動停止（Timeout），可以選擇在這裡重新 Start()
        if (cause == DictationCompletionCause.Complete)
        {
            dictationRecognizer.Start();
        }
    }

    private void OnDictationError(string error, int hresult)
    {
        Debug.LogError($"語音辨識出錯: {error} (HResult: {hresult})");
    }

    private void OnDestroy()
    {
        // 腳本被銷毀時，一定要釋放資源，否則麥克風會被佔用
        if (dictationRecognizer != null)
        {
            dictationRecognizer.DictationHypothesis -= OnDictationHypothesis;
            dictationRecognizer.DictationResult -= OnDictationResult;
            dictationRecognizer.DictationComplete -= OnDictationComplete;
            dictationRecognizer.DictationError -= OnDictationError;
            
            if (dictationRecognizer.Status == SpeechSystemStatus.Running)
            {
                dictationRecognizer.Stop();
            }
            dictationRecognizer.Dispose();
        }
    }
}