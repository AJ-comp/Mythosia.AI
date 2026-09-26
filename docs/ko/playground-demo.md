# Playground 둘러보기

Playground를 사용하면 애플리케이션 코드를 작성하기 전에 모델 옵션을 비교하고 문서 검색을 설정할 수 있습니다. 이 영상은 로컬 작업 공간의 현재 화면에서 모델을 찾고 RAG 파이프라인 설정을 살펴보는 과정을 보여줍니다.

<video class="playground-video" controls playsinline preload="metadata" poster="../assets/playground-demo.png" aria-label="Mythosia.AI Playground 화면 사용 안내 영상">
  <source src="../assets/playground-demo.mp4?v=3" type="video/mp4">
  <track kind="subtitles" src="../assets/playground-demo.vtt?v=3" srclang="en" label="English">
  <track kind="subtitles" src="../assets/playground-demo.ko.vtt?v=3" srclang="ko" label="한국어" default>
  <track kind="subtitles" src="../assets/playground-demo.ja.vtt?v=3" srclang="ja" label="日本語">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hans.vtt?v=3" srclang="zh-Hans" label="简体中文">
  <track kind="subtitles" src="../assets/playground-demo.zh-Hant.vtt?v=3" srclang="zh-Hant" label="繁體中文">
  <track kind="subtitles" src="../assets/playground-demo.de.vtt?v=3" srclang="de" label="Deutsch">
  <track kind="subtitles" src="../assets/playground-demo.fr.vtt?v=3" srclang="fr" label="Français">
  <track kind="subtitles" src="../assets/playground-demo.es.vtt?v=3" srclang="es" label="Español">
  <track kind="subtitles" src="../assets/playground-demo.pt.vtt?v=3" srclang="pt" label="Português">
  <track kind="subtitles" src="../assets/playground-demo.ru.vtt?v=3" srclang="ru" label="Русский">
  <track kind="subtitles" src="../assets/playground-demo.uk.vtt?v=3" srclang="uk" label="Українська">
  <track kind="subtitles" src="../assets/playground-demo.vi.vtt?v=3" srclang="vi" label="Tiếng Việt">
  <track kind="subtitles" src="../assets/playground-demo.th.vtt?v=3" srclang="th" label="ไทย">
  이 브라우저는 삽입된 동영상 재생을 지원하지 않습니다. <a href="../assets/playground-demo.mp4?v=3">사용 안내 영상 다운로드</a>.
</video>

[영상 다운로드 (MP4)](../assets/playground-demo.mp4?v=3) · [자막 읽기](../assets/playground-demo.ko.vtt?v=3)

이 영상에는 음성이 없습니다. 페이지 언어에 맞는 자막이 자동으로 표시되며, 플레이어 메뉴에서 자막 언어를 바꾸거나 끌 수 있습니다.

## 영상에서 확인할 수 있는 내용

1. 7개 공급자 그룹을 탐색하고 모델 이름이나 공급자로 검색합니다.
2. 모델을 연결하기 전에 공급자 키 입력 창을 엽니다.
3. 영어와 한국어 사이를 전환합니다. 인터페이스는 13개 언어를 지원합니다.
4. 문서 등록과 텍스트 분할 옵션을 살펴봅니다.
5. 임베딩 공급자, 벡터 저장소, 하이브리드 검색과 재랭킹 설정을 확인합니다.

이 영상은 화면 사용 안내입니다. API 키를 전송하거나 문서를 인덱싱하거나 모델 응답을 생성하지 않습니다.

## 로컬에서 실행하기

`global.json`에 지정된 SDK를 설치한 뒤 저장소 루트에서 다음 명령을 실행합니다.

```sh
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

애플리케이션이 출력한 주소를 엽니다. 먼저 모델과 설정을 살펴보고, 요청을 보낼 준비가 되면 공급자 키를 추가합니다.

연결, 언어와 로컬 개발에 관한 자세한 내용은 [Playground 안내서](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md)를 참고하세요. 관련 라이브러리 API는 [빠른 시작](getting-started.md) 또는 [RAG 기초](rag.md)에서 확인할 수 있습니다.
