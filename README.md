# Therabby Clinical Tools

Open-source, browser-based tools for occupational therapy, rehabilitation practice, clinical education, activity analysis, accessibility training, and participation support.

> **Status:** Early-stage OSS project. These tools are intended for education, activity design, and clinical support. They do **not** replace clinical judgment, diagnosis, or professional assessment.

## Purpose

Rehabilitation clinicians often need small, adaptable digital tools that can be inspected, modified, and used without vendor lock-in. Many practical tools in occupational therapy remain paper-based, proprietary, or difficult to adapt to local clinical contexts.

Therabby Clinical Tools aims to provide a portfolio of lightweight web tools that therapists, educators, and community practitioners can use, modify, and improve together.

## Initial Tools

| Tool | Purpose | Folder |
|---|---|---|
| Activity Analysis Builder | Create simple activity analysis notes for occupational therapy education and practice | `apps/activity-analysis` |
| Visual Scanning Trainer | Simple browser task for visual search/scanning practice and observation | `apps/visual-scanning` |
| One-Hand Interaction Trainer | Practice basic one-hand pointer interactions in a browser | `apps/one-hand-trainer` |

## Clinical Positioning

These tools are designed for:

- occupational therapy education
- activity analysis
- patient and family education
- rehabilitation task design
- accessibility exploration
- local clinical workflow support

These tools are **not** designed for:

- medical diagnosis
- automated clinical decision-making
- emergency use
- replacing standardized assessments
- collecting personal health information without appropriate governance

See [`docs/CLINICAL_DISCLAIMER.md`](docs/CLINICAL_DISCLAIMER.md).

## Demo

Open `index.html` in a browser, or host the repository with GitHub Pages.

## Privacy by Design

The initial tools run locally in the browser. They do not require login and do not send data to a server by default.

Future features involving logs, exports, or AI support should follow these principles:

- local-first where possible
- no unnecessary personal data collection
- clear consent and explanation
- no hidden tracking
- anonymization or pseudonymization when research data are used
- institutional review where required

## Repository Structure

```text
therabby-clinical-tools/
├── index.html
├── style.css
├── apps/
│   ├── activity-analysis/
│   ├── visual-scanning/
│   └── one-hand-trainer/
├── docs/
│   ├── CLINICAL_DISCLAIMER.md
│   ├── ROADMAP.md
│   └── PRIVACY.md
├── CONTRIBUTING.md
├── SECURITY.md
├── CODE_OF_CONDUCT.md
└── LICENSE
```

## Roadmap

- Add reusable UI components for clinical web tools
- Improve accessibility and keyboard support
- Add offline-first behavior
- Add export functions that avoid unnecessary personal data
- Add Japanese documentation
- Add example clinical use cases based on occupational therapy frameworks such as ICF, MOHO, PEO, CMOP-E, and activity analysis
- Add tests and accessibility checks

## Contributing

Contributions are welcome from occupational therapists, rehabilitation professionals, educators, designers, developers, and people with lived experience.

Please read [`CONTRIBUTING.md`](CONTRIBUTING.md) before submitting issues or pull requests.

## License

MIT License.
