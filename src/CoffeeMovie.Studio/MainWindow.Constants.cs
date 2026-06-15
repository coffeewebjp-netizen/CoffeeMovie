using System;
using System.Collections.Generic;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private const string FlagTagName = "flag";
    private const string DefaultTranslationCommand = "codex-spark";
    private const string DefaultCodexSparkModel = "gpt-5.3-codex-spark";
    private const string DefaultEnglishSubtitleOverlayPosition = "below2";
    private const string DefaultJapaneseSubtitleOverlayPosition = "below1";
    private const string DefaultAiNoteOverlayPosition = "above1";
    private const string DefaultUserNoteOverlayPosition = "above2";
    private const string DefaultLearningNotesAudienceLevel = "B1";
    private const string DefaultTranslationArguments = "exec --full-auto -C \"{outputDir}\" --add-dir \"{inputDir}\" --skip-git-repo-check \"You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, translate {input}, and write the Japanese SRT to {output}.\"";
    private const string DefaultLearningNotesArguments = "exec --full-auto -C \"{outputDir}\" --add-dir \"{inputDir}\" --skip-git-repo-check \"You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, analyze {input}, and write sparse learning notes JSON to {notesOutput}. Do not generate the JSON with a PowerShell/Python classification script.\"";
    private const string DefaultTranslationPrompt = """
あなたはアニメ英語字幕を日本語字幕へ翻訳する専門エージェントです。

目的:
- 英語SRT `{input}` を読み、自然で作品世界に合う日本語SRTを `{output}` に書き出してください。
- source language: `{source}`
- target language: `{target}`
- movie title: `{title}`

絶対条件:
- SRTの番号、順番、開始時刻、終了時刻を変更しないでください。
- キュー数を増減しないでください。
- 出力は有効なSRT本文だけにしてください。Markdown、説明、注釈、コードブロックは禁止です。
- タイムスタンプは入力SRTのものをそのまま使ってください。
- 英語本文だけを日本語へ置き換えてください。
- `{input}` と `{output}` が相対パスの場合は現在の作業フォルダ基準で扱い、絶対パスへ変換しないでください。

翻訳方針:
- 機械的な直訳ではなく、日本の視聴者が字幕として自然に読める表現へ意訳してください。
- 作品の世界観を推定してください。ファンタジー、現代劇、SFなどに合う語彙を選んでください。
- キャラクターの口調を文脈から判断し、セリフへ反映してください。
- 英語のイディオム、スラング、皮肉、冗談は直訳せず、意味と感情が伝わる日本語へ変換してください。
- 固有名詞、魔法名、地名、組織名は一貫させてください。判断できない場合は自然なカタカナまたは原語維持を選んでください。
- 字幕として読みやすい長さを意識し、冗長な説明を避けてください。

品質基準:
- 会話として自然であること。
- キャラクターの距離感、年齢感、敬語/ため口が破綻しないこと。
- 原文の意味を削りすぎず、字幕としてテンポよく読めること。
- 各キューの日本語が、そのタイムスタンプ内で読める長さであること。
""";
    private const string DefaultLearningNotesPrompt = """
あなたは英語学習者向けにアニメ英語字幕を分析する専門エージェントです。

目的:
- 英語SRT `{input}` を読み、対象レベル `{audienceLevel}` の学習者に本当に役立つ学習メモを `{notesOutput}` にJSONで書き出してください。
- movie title: `{title}`
- source language: `{source}`
- audience level: `{audienceLevel}`

出力形式:
- `{notesOutput}` には有効なUTF-8 JSONだけを書いてください。Markdown、説明、コードブロックは禁止です。
- ルートは配列にしてください。
- 重要なキューだけを出力してください。コメント不要キューは配列に含めないでください。
- 未出力のキューはアプリ側でコメント不要として扱い、既存AIメモを消します。
- `index` はSRT番号と同じ整数にしてください。
- `cefr` は `A1`, `A2`, `B1`, `B2`, `C1`, `C2` のいずれかにしてください。
- `focus` は、そのキュー本文に実在する英単語または英語フレーズを完全一致で入れてください。
- `note` は必ず文字列で埋めてください。
- 重要なキューの `note` は日本語で100文字以内にしてください。

JSONスキーマ:
[
  {
    "index": 1,
    "cefr": "B1",
    "focus": "dissipate",
    "note": "CEFR B1: 語彙 'dissipate'=魔力が散る。魔法説明で再登場しやすい世界観語。"
  }
]

分析方針:
- 学習者の対象レベルは `{audienceLevel}` です。対象レベル未満の普通の挨拶、短い相づち、固有名詞だけの行は出力しないでください。
- 対象レベル以上の語彙・構文・慣用句・口語、または対象レベル未満でも作品理解や今後の読解に効く特殊表現があるキューだけ実質的なnoteを書いてください。
- 世界観ならではの語彙、魔法/戦闘/宗教/旅/師弟関係などのジャンル語、キャラクターの口調が出る言い回しを優先してください。
- `Hello`, `Yes`, `Okay`, `No`, `Apple` のような基礎語や一語返答は、特殊なニュアンスがない限り解説しないでください。
- noteの先頭に `CEFR {level}:` を含め、続けて `語彙:`, `構文:`, `慣用句:`, `口語:`, `世界観:` など要点が分かるラベルを入れてください。
- 各noteは必ずそのキュー固有の内容にしてください。汎用テンプレートの反復は禁止です。
- `$k`, `{word}`, `{level}`, `など抽象語`, `基本表現` のような未展開テンプレートや曖昧な定型句をnoteに書かないでください。
- コメント不要ではないnoteには、`focus` と同じ英単語または英語フレーズを必ず引用してください。別キューの語句を書かないでください。
- `focus` は現在のキュー本文だけから選んでください。前後のキューは文脈理解に使ってもよいですが、前後のキューにしか存在しない語句・構文・慣用句を現在キューのnoteに書くことは禁止です。
- noteを書いたあと、`focus` が現在キュー本文に完全一致で含まれるか自己確認してください。含まれない場合は、そのキューをコメント不要マーカーに戻してください。
- `By this corrupt priest.` のような断片キューでは、前の文の受動態や構文を説明しないでください。断片自体に有用な語彙がなければコメント不要にしてください。
- `Thank you very much`, `why do you like`, `Magic is amazing`, `isn't it?`, `I find that hard to believe` のような通常会話は、特殊なニュアンスや作品固有性がない限りコメント不要にしてください。
- B1以上に上げている語彙、文脈、構文、慣用句がある場合は、該当する英単語や英語フレーズを必ず明記してください。
- CEFRとは別に、よく使うスラング、口語表現、慣用句があれば `スラング:`、`口語:`、`慣用句:` のように示してください。
- 明らかに字幕認識ミスの疑いがある不自然な英語は、無理に通常解説せず `CEFR B1: ASR疑い: 'focus' は文脈上不自然。字幕修正候補として確認。` の形にしてください。
- ローマ字歌詞、固有名詞だけ、英語学習対象外に近いキューは出力しないでください。
- 実質的なnoteは全体の8%-25%を目安にしてください。どれだけ密度が高くても30%を超えないでください。
- 同じnote文を大量に使い回さないでください。似たキューでも、該当語句と理由を変えてください。
- 全キュー分の雛形JSON、PowerShell/Python等の自動分類スクリプト、辞書上書き方式で作らないでください。SRT本文を読んで、重要なキュー番号だけを選んでください。
- セリフ本文、番号、時刻は変更しないでください。字幕ファイルを書き換えないでください。
- `{input}` と `{notesOutput}` が相対パスの場合は現在の作業フォルダ基準で扱い、絶対パスへ変換しないでください。
""";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".mov",
        ".webm",
        ".mkv",
        ".avi"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".vtt"
    };

}