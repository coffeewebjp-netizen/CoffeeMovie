# CoffeeLearning Integration

CoffeeMovie Reader and CoffeeMovie Studio can register the currently active subtitle cue into CoffeeLearning as a vocabulary item. CoffeeMovie stores a CoffeeLearning API URL, a target deck id, and a Bearer authentication header, then calls CoffeeLearning's word registration API.

## User Flow

1. Open CoffeeLearning in the normal browser that already has your CoffeeLearning login session.
2. In CoffeeMovie Studio, open CoffeeLearning settings.
3. Click the browser auto-acquire button. Studio starts a temporary localhost receiver and opens the normal browser.
4. CoffeeLearning issues a CoffeeMovie token and redirects the browser to Studio localhost. Studio fills the Authorization header automatically.
5. Confirm the target `deckId`, then save.
6. While previewing a video, click the word registration button or the active subtitle card.

Manual token copy is still available as a fallback: issue a CoffeeMovie token in CoffeeLearning settings and paste the full `Authorization` value, for example `Bearer cmt_...`.

Embedded WebView login is not the preferred path. Google sign-in inside embedded WebViews can be blocked by Google's secure-browser policy, so the normal browser handoff is the stable PC flow.

## Studio PC Flow

1. Open CoffeeLearning in the normal PC browser and make sure you are logged in.
2. In CoffeeMovie Studio, click the CoffeeLearning settings button in the header.
3. Keep API URL as `https://www.coffeewebjp.com` unless you are testing another environment.
4. Click the browser auto-acquire button.
5. When the Authorization header is filled, confirm `deckId` and save.
6. Play or seek to a subtitle cue in the edit preview, full preview, or detached preview window.
7. Click the word registration button or subtitle card to register the matching English cue.

Studio uses the same payload as Reader. English subtitle text becomes `word`, the matched Japanese subtitle becomes `meaning`, AI/user notes become `memo`, parsed CEFR/CERF becomes `cefr`, and the combined movie tags plus cue tags are sent as `labelNames`. If no Japanese cue can be matched, Studio prompts for the meaning before calling CoffeeLearning.

## Browser Token Handoff

The PC handoff avoids reading browser cookies directly:

1. Studio starts a temporary HTTP listener on `127.0.0.1`.
2. Studio opens CoffeeLearning at `/api/coffee-movie/studio-connect` with `redirect_uri`, `state`, and `name`.
3. CoffeeLearning validates that `redirect_uri` is loopback-only and uses the normal browser session.
4. CoffeeLearning issues a CoffeeMovie Bearer token and redirects to Studio's localhost callback with the token in the URL fragment.
5. Studio serves a local relay page that posts `state`, `bearer`, and `deckId` back to itself.
6. Studio verifies `state` before accepting the token, then the browser tab can be closed.

If the browser is not logged in, CoffeeLearning shows a login instruction page. Log in in that browser, then click the auto-acquire button again. If the browser does not redirect automatically, click the displayed return link.

## Data Mapping

CoffeeMovie sends one `POST /words` request to CoffeeLearning. Studio and Reader both delegate the HTTP payload construction, auth-header handling, label normalization, and response parsing to the shared `CoffeeLearningRegistrationClient` in `CoffeeMovie.Core`.

| CoffeeMovie source | CoffeeLearning field |
| --- | --- |
| Active English subtitle cue text | `word` |
| Matching Japanese subtitle cue text | `meaning` |
| Cue AI note plus user note | `memo` |
| CEFR/CERF value parsed from memo | `cefr` |
| Movie tags plus cue tags from the learning panel | `labelNames` |
| Configured CoffeeLearning deck | `deckId` |

If no Japanese cue can be matched, the app prompts the user to enter `meaning` before registration.

## Labels

Movie tags and cue tags are sent together as `labelNames`. CoffeeLearning resolves those names to labels in the target deck, creating missing labels when necessary. This keeps CoffeeMovie independent from CoffeeLearning label ids.

Examples:

```json
{
  "word": "I could not bring myself to say it.",
  "meaning": "I could not find the courage to say that.",
  "memo": "CEFR: B2\nUseful emotional expression.",
  "cefr": "B2",
  "labelNames": ["series-title", "emotion", "phrase"],
  "deckId": "deck-english-main"
}
```

Existing CoffeeLearning registrations are not backfilled with labels. Label attachment applies to registrations created after this feature is installed.

## Tag Management

Studio has a tag management screen in the header. Use it to create, rename, reorder, or delete movie tags and subtitle tags separately. Renaming or deleting a tag updates existing movie metadata and cue learning-state tags, so CoffeeLearning registrations made after the change use the current tag names.

The movie metadata editor also has a tag selector for movie tags. Subtitle/cue tags continue to be edited from the learning panel and scene rows.

## Word Scoring

CoffeeMovie can score a word before sending it to CoffeeLearning.

- Studio default: `AIAGENT (PC)`. The CoffeeLearning settings window has fields for command, model, and arguments. The default uses the same `codex-spark` style command path as subtitle translation and writes strict JSON with `judgement`, `cefr`, `point`, `better_meaning`, and `diagnosis`.
- Reader default: `AI provider`. The Reader "Other" menu has `CoffeeLearning scoring`, where GPT/OpenAI, Gemini, DeepSeek, or a local OpenAI-compatible LLM can be configured. The provider API key is saved in platform secure storage.
- Compatibility options: `CoffeeLearning API` keeps the previous server-side `autoAnalyze=true` behavior, and `Simple estimate` sends only the local fallback CEFR/point.

When AIAGENT or an AI provider succeeds, CoffeeMovie sends the AI `cefr` and `point` to CoffeeLearning with `autoAnalyze=false`, so the server does not overwrite the score. The AI judgement/diagnosis is appended to the memo. If scoring fails, registration continues with the simple fallback score rather than blocking word registration.
## Registered State

CoffeeMovie marks a cue as registered only after CoffeeLearning returns a successful response. It stores:

- `CoffeeLearningRegisteredAt`
- `CoffeeLearningWordId`
- `CoffeeLearningDeckId`

The player button then shows the registered state and prevents accidental duplicate registration.

## Related Reader Behavior

Reader also stores local playback and cue learning state. If the app closes mid-video, resume behavior uses the locally saved playback position rather than starting from the beginning. CoffeeLearning registration state travels with the Reader learning-state backup and sidecar persistence paths already used by cue notes and tags.

## Build And Install

```powershell
cd C:\work\CoffeeMovie
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -f net10.0-android -c Debug
adb -s <device> install -r -d src\CoffeeMovie.Reader\bin\Debug\net10.0-android\net.coffeewebjp.coffeemovie.reader-Signed.apk
```

For Studio MSI packaging, use:

```powershell
cd C:\work\CoffeeMovie
.\scripts\windows\New-CoffeeMovieStudioMsi.ps1
```

## Troubleshooting

- App says success but the word is not visible: check that CoffeeMovie's configured `deckId` matches the deck currently shown in CoffeeLearning, and clear any CoffeeLearning search/filter.
- Registration fails with 401 or 403: use browser auto-acquire again, or issue a new CoffeeLearning token and paste the full `Bearer cmt_...` header.
- Labels are missing: confirm the movie and cue had tags before registering. Tags are read at registration time.