# Standards and frameworks

| Standard | Current version / edition | Note |
|---|---|---|
| OWASP Top 10 | 2025 edition | New A03 Software Supply Chain Failures and A10 Mishandling of Exceptional Conditions; RC1 Nov 2025, finalized early 2026 |
| OWASP ASVS | 5.0.0 (May 2025) | Major rewrite: ~350 requirements, 17 chapters (V1–V17), renumbered IDs — do not cite 4.x numbering |
| OWASP API Security Top 10 | 2023 | Still latest |
| CWE Top 25 | 2025 (Dec 2025) | XSS #1, SQLi #2; six new entries incl. buffer-overflow CWEs |
| OWASP LLM/GenAI Top 10 | LLM Top 10 "2025" (v2.0, Nov 2024) | Current edition of the LLM list under the OWASP GenAI project |
| OWASP Top 10 for Agentic Applications | For 2026 (released Dec 2025) | ASI01–ASI10; the agentic companion list under the OWASP GenAI project |
| NIST SSDF (SP 800-218) | v1.1 (Feb 2022) | v1.2/r1 is still a draft (ipd Dec 2025) — cite 1.1 as current |
| SLSA | v1.2 (Nov 2025) | Adds normative Source Track (L1–L4) alongside Build Track (L0–L3) |
| NIST CSF | 2.0 (Feb 2024) | Govern function added |
| Zero Trust | NIST SP 800-207 (2020) + SP 1800-35 implementation guide (final Jun 2025) | |
| TLS | TLS 1.3 = RFC 8446; deployment guidance BCP 195 / RFC 9325 (TLS 1.2 minimum, prefer 1.3); NIST SP 800-52r2 | |
| NIST PQC standards | FIPS 203 (ML-KEM), FIPS 204 (ML-DSA), FIPS 205 (SLH-DSA) — all final Aug 2024 | NIST IR 8547 (2025): classical public-key algorithms deprecated by 2030, disallowed after 2035 |
| PQC in protocols | RFC 9881 (ML-DSA certificates in X.509, Oct 2025) | Hybrid TLS key exchange (draft-ietf-tls-ecdhe-mlkem) still an IETF draft though widely deployed — deployment state in the networking pins below |
| Digital identity | NIST SP 800-63-4 (final Jul 2025) | Four-volume digital identity guidelines revision |
| OAuth security | RFC 9700 / BCP 240 (OAuth 2.0 Security BCP, Jan 2025); RFC 9449 (DPoP) | OAuth 2.1 and OAuth for Browser-Based Apps still IETF drafts; browser apps: BFF recommended |
| PCI DSS | 4.0.1 | Future-dated requirements mandatory since Mar 2025 |
| OWASP Application Logging Vocabulary | Living cheat sheet, unversioned | Stable event-name vocabulary for security logging |
| HTTP/3 / QUIC | RFC 9114 / RFC 9000 | Stable |
| IPv6 | RFC 8200 / STD 86 | Full Internet Standard |
| Wi-Fi | Wi-Fi 7 (802.11be-2024) current | Wi-Fi 8 (802.11bn) draft, certification ~2027 |
| ISO/IEC 25010 | :2023 (Nov 2023) | Nine characteristics; Safety added; usability/portability replaced by interaction capability/flexibility; quality-in-use split to ISO/IEC 25019:2023 |
| ISTQB CTFL | v4.0 (2023); maintenance v4.0.1 (Sep 2024, content unchanged) | Seven testing principles |
| WCAG | 2.2 (W3C Recommendation, Oct 2023) | WCAG 3.0 Working Draft (~2028+, will coexist); EN 301 549 references WCAG 2.1 AA (v3.2.1) — 2.2-aligned revision expected 2026 |
| OpenTelemetry | Traces, metrics, logs stable | Profiles signal alpha (Mar 2026), GA targeted Q3 2026; W3C Trace Context is the propagation standard |
| Web performance metrics | Core Web Vitals (LCP, INP, CLS) | Current metric set |
| Idempotency-Key header | IETF draft | Cite as a convention, not a finished standard |
| TMMi | Model v2.0 | R1.3-based assessments sunset Aug 2027 |
| TOGAF | 10th Edition (Apr 2022) | Modular Fundamental Content + Series Guides |
| iSAQB CPSA-F | v2025.1 (mandatory since Apr 2025) | Restructured around architect tasks; next release 2027 |
| iSAQB CPSA-A | ~20 modules versioned independently | ADOC revised 2025 |
| C4 model | Unversioned living model | c4model.com canonical |
| arc42 | v8.2 (Jan 2023) | Still current |
| PMBOK Guide | 8th Edition (digital Nov 2025; print Jan 2026) | Principles + domains, 5 Focus Areas, 40 non-prescriptive processes |
| PMP ECO | 2026 ECO (exam live Jul 2026) | People 33% / Process 41% / Business Env 26%; ~60% agile-hybrid |
| Standard for Program Mgmt (PgMP) | Fifth Edition (Apr 2024) | ANSI/PMI 08-002-2024; new Collaboration performance domain |
| Scrum Guide | Nov 2020 | 2025 "Expansion Pack" (Jun 2025) is a supplement, not a revision |
| SAFe | SAFe 6.0 (version numbers retired) | "AI-Native SAFe" operating model (Jun 2026) sits atop SAFe 6 |
| Practice Standard for WBS | Third Edition (2019) | PMI; extends WBS practice to agile, iterative, incremental, and predictive life cycles |
| MADR | 4.0.0 (Sep 2024) | ADR template; 4.0 renamed Validation → Confirmation (under Decision Outcome) and Deciders → decision-makers |
| Mermaid | 11.x (11.16.0, Jun 2026) | Text-to-diagram standard for docs; v11 line adds new diagram types steadily — pin renders to a tested minor |
| Diátaxis | Unversioned living framework | diataxis.fr canonical; four documentation modes: tutorial, how-to, reference, explanation |
| HTTP semantics | RFC 9110 / 9111 (Jun 2022) | The method-semantics and caching RFCs REST contracts cite; error shape is Problem Details, RFC 9457 |
| OpenAPI | 3.2.0 (Sep 2025) | Adds QUERY method, streaming media types (SSE, JSON Lines), hierarchical tags, device authorization flow; backward-compatible with 3.0/3.1 |
| AsyncAPI | 3.1.0 (current; 3.0 Dec 2023 was the operations/channels split) | Contract spec for event-driven/channel APIs |
| GraphQL | September 2025 edition | First edition since Oct 2021: schema coordinates, @oneOf inputs, executable-document descriptions |
| Deprecation / Sunset headers | RFC 9745 (Mar 2025) / RFC 8594 (2019) | Machine-readable endpoint deprecation and retirement signaling |
| SPDX | Spec 3.0.1; license list 3.28 (Feb 2026) | ISO/IEC 5962:2021 standardizes SPDX 2.2.1; identifiers and expressions are what makes license policy decidable |
| MessageFormat 2.0 | Spec stable in CLDR 47 (Mar 2025) | ICU 78 (Oct 2025): Java core API at "draft", C++ in tech preview — ICU MessageFormat 1 remains the production default |
| DORA metrics | 2025 report — five metrics | Deployment frequency, lead time for changes, change failure rate, failed-deployment recovery time, rework rate (added 2025) |
| Kanban Guide | May 2025 revision (kanbanguides.org) | Definition of Workflow; mandatory flow measures: WIP, throughput, cycle time, work item age |
| Practice Standard for Project Estimating | Second Edition (PMI, 2019) | Covers plan-driven and agile/change-driven estimating |
| Building Evolutionary Architectures | 2nd Edition (O'Reilly, 2022) | Ford/Parsons/Kua/Sadalage — the fitness-function vocabulary architecture evaluation uses |
| EDPB pseudonymisation | Guidelines 01/2025 (adopted for consultation 16 Jan 2025) | Three-part identifiability test (singling out, linkability, inference); pseudonymised data remains personal data; final post-consultation edition pending — verify before citing as final |
| NIST key management | SP 800-57 Part 1 Rev. 5 (May 2020) | Key-strength and key-lifecycle vocabulary (key states, cryptoperiods) |
| Kubernetes Pod Security Standards | Privileged / Baseline / Restricted; Pod Security Admission stable since K8s 1.25 | Unversioned policy levels, maintained with each Kubernetes release |
| Standard Webhooks | v1.0.0 | Community convention for webhook signing and headers (webhook-id / -timestamp / -signature, HMAC-SHA256) |
| NIST AI RMF | 1.0 (Jan 2023) + Generative AI Profile, NIST-AI-600-1 (Jul 2024) | Govern/Map/Measure/Manage; the GenAI profile adds generative-risk categories and actions |
| EU AI Act | Regulation (EU) 2024/1689 | GPAI obligations apply since 2 Aug 2025; AI Office enforcement and fines (up to 3% global turnover / EUR 15M) from 2 Aug 2026; models placed before Aug 2025 compliant by 2 Aug 2027; the AI omnibus (proposal Nov 2025, political agreement 7 May 2026) defers high-risk timelines only — GPAI dates unchanged |
| EU AI Act GPAI thresholds | 10^23 FLOP training-compute presumption of GPAI; 10^25 FLOP systemic-risk presumption | Commission GPAI guidelines (Jul 2025); a downstream modifier becomes a provider at roughly one third of the original model's training compute |
| EU AI Act GPAI instruments | Code of Practice (10 Jul 2025); public training-content summary template (24 Jul 2025) | The Art. 53 artifact set: technical documentation, downstream-provider information, copyright policy, public training-content summary |
| MLCommons AILuminate | v1.1 suite (v1.0 report Feb 2025) | 12-hazard taxonomy (physical / nonphysical / contextual); ~24k prompts per language, EN + FR |
