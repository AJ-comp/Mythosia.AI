---
_layout: landing
_disableToc: true
_disableAffix: true
---

<div class="hero-section">
  <div class="hero-content">
    <div class="hero-badge">Open Source · .NET · NuGet</div>
    <h1 class="hero-title">Mythosia<span class="hero-accent">.AI</span></h1>
    <p class="hero-subtitle">
      A modular .NET AI library for building intelligent applications.<br>
      Switch providers, add RAG, load documents — all with a unified API.
    </p>
    <div class="hero-actions">
      <a href="#" id="btn-get-started" class="btn-primary-hero">Get Started</a>
      <a href="api/index.md" class="btn-secondary-hero">API Reference</a>
      <a href="https://github.com/AJ-comp/Mythosia.AI" class="btn-ghost-hero" target="_blank">GitHub ↗</a>
    </div>
  </div>
</div>

<div class="features-section">
  <div class="section-label">What's Included</div>
  <h2 class="section-title">Everything you need to build AI apps</h2>
  <div class="features-grid">

    <div class="feature-card">
      <div class="feature-icon feature-icon-blue">⚡</div>
      <h3>Core</h3>
      <p>Unified AI abstractions with implementations for OpenAI, Claude, Gemini, DeepSeek, Grok, and more. One interface, any provider.</p>
      <a href="docs/completions.md" class="feature-link">Explore Core →</a>
    </div>

    <div class="feature-card">
      <div class="feature-icon feature-icon-purple">🔍</div>
      <h3>RAG</h3>
      <p>Full retrieval-augmented generation pipeline — document splitting, embeddings, vector search, reranking, and diagnostics.</p>
      <a href="docs/rag.md" class="feature-link">Explore RAG →</a>
    </div>

    <div class="feature-card">
      <div class="feature-icon feature-icon-green">📄</div>
      <h3>Document Loaders</h3>
      <p>Extract and normalize content from Word, Excel, PowerPoint, and PDF files with a simple, consistent loader API.</p>
      <a href="docs/document-loaders.md" class="feature-link">Explore Loaders →</a>
    </div>

    <div class="feature-card">
      <div class="feature-icon feature-icon-orange">🗄️</div>
      <h3>Vector Database</h3>
      <p>Pluggable vector store layer supporting Qdrant, Pinecone, PostgreSQL, and in-memory backends.</p>
      <a href="docs/rag-advanced.md" class="feature-link">Explore VectorDB →</a>
    </div>

  </div>
</div>

<div class="quickstart-section">
  <div class="quickstart-inner">
    <div class="quickstart-text">
      <div class="section-label">Quick Install</div>
      <h2>Up and running in seconds</h2>
      <p>Install the core package and your provider of choice, then start building.</p>
      <a href="#" id="btn-getting-started-guide" class="btn-primary-hero" style="margin-top: 1rem; display: inline-block;">Full Getting Started Guide →</a>
    </div>
    <div class="quickstart-code">
      <div class="code-block">
        <div class="code-header">Package Manager</div>
        <pre><code>dotnet add package Mythosia.AI</code></pre>
        <pre><code>dotnet add package Mythosia.AI.Rag</code></pre>
        <pre><code>dotnet add package Mythosia.VectorDb.Qdrant</code></pre>
      </div>
    </div>
  </div>
</div>

<div class="packages-section">
  <div class="section-label">NuGet Packages</div>
  <h2 class="section-title">Pick what you need</h2>
  <div class="packages-grid">
    <div class="package-group">
      <h4>Core</h4>
      <ul>
        <li><code>Mythosia.AI</code></li>
        <li><code>Mythosia.AI.Abstractions</code></li>
        <li><code>Mythosia.AI.Providers.Alibaba</code></li>
      </ul>
    </div>
    <div class="package-group">
      <h4>RAG</h4>
      <ul>
        <li><code>Mythosia.AI.Rag</code></li>
        <li><code>Mythosia.AI.Rag.Abstractions</code></li>
      </ul>
    </div>
    <div class="package-group">
      <h4>Document Loaders</h4>
      <ul>
        <li><code>Mythosia.Documents.Abstractions</code></li>
        <li><code>Mythosia.Documents.Office</code></li>
        <li><code>Mythosia.Documents.Pdf</code></li>
      </ul>
    </div>
    <div class="package-group">
      <h4>Vector Database</h4>
      <ul>
        <li><code>Mythosia.VectorDb.Abstractions</code></li>
        <li><code>Mythosia.VectorDb.InMemory</code></li>
        <li><code>Mythosia.VectorDb.Pinecone</code></li>
        <li><code>Mythosia.VectorDb.Postgres</code></li>
        <li><code>Mythosia.VectorDb.Qdrant</code></li>
      </ul>
    </div>
  </div>
</div>

<script>
(function () {
  const LANG_MAP = {
    ko: { intro: 'docs/ko/introduction.html', start: 'docs/ko/getting-started.html' },
    ja: { intro: 'docs/ja/introduction.html', start: 'docs/ja/getting-started.html' },
    vi: { intro: 'docs/vi/introduction.html', start: 'docs/vi/getting-started.html' },
    th: { intro: 'docs/th/introduction.html', start: 'docs/th/getting-started.html' },
    pt: { intro: 'docs/pt/introduction.html', start: 'docs/pt/getting-started.html' },
    es: { intro: 'docs/es/introduction.html', start: 'docs/es/getting-started.html' },
  };
  const DEFAULT = { intro: 'docs/introduction.html', start: 'docs/getting-started.html' };

  function detectLang() {
    const saved = localStorage.getItem('mythosia-lang');
    if (saved) return saved;
    return (navigator.language || 'en').slice(0, 2).toLowerCase();
  }

  document.addEventListener('DOMContentLoaded', function () {
    const paths = LANG_MAP[detectLang()] || DEFAULT;
    const btnStart = document.getElementById('btn-get-started');
    const btnGuide = document.getElementById('btn-getting-started-guide');
    if (btnStart) btnStart.href = paths.intro;
    if (btnGuide) btnGuide.href = paths.start;
  });
})();
</script>
