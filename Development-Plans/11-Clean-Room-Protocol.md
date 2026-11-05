# 11 — Clean-Room Protocol

**Purpose.** The working rules that keep this implementation independent and defensible. This is the one manual that constrains how everyone else works.

**Applies to:** every contributor, contractor, designer and tester. · **Related:** [05](05-Security-and-Compliance.md) §8, [09](09-Risks-and-Open-Questions.md) §3 · **Index:** [00-Contents.md](00-Contents.md)

---

## 1. What "clean room" means here

Two separated activities:

| Team | May see | Produces |
|---|---|---|
| **Research** | Public documentation, public marketing pages, published API references, public pricing | Requirements and behavioural notes: what happens, in what order, under what condition |
| **Implementation** | Only the resulting behavioural notes and this manual set | Original design and code |

The separation is what makes the claim credible. A single engineer who both reads the competitor's CSS and writes ours has destroyed the distinction, no matter how careful they were.

In practice on a small team the same person may do both — but then the **sources** must still be public-only, and the behavioural notes must be written in prose, never as markup, selectors or asset references.

---

## 2. The line, stated precisely

| Permitted | Prohibited |
|---|---|
| Reading public support articles, API docs, pricing pages | Reading their source code, minified bundles, or decompiled assemblies |
| Recording behaviour: "the kanban groups by status label and can hide empty lanes" | Recording markup, CSS selectors, class names or asset URLs |
| Reproducing a *functional* outcome | Reproducing a *visual* expression (palette, spacing, iconography, type) |
| Using their public API to understand data shapes | Using their product's assets, fonts, images or copy at all |
| Naming a behaviour by its function | Using their trademarks, product names or wordmark anywhere |
| Selling a product that does similar things | Implying affiliation, endorsement or compatibility |

The functional/expressive distinction is the whole game. A kanban board is an idea; their particular kanban card is an expression.

---

## 3. Rules for the research team

1. **Public sources only.** No authenticated scraping of app UI, no trial account reverse-engineering, no reading of client bundles to extract CSS values.
2. **Record the source, the date and the claim** in the register ([13](13-Source-Register.md)). A claim without a source is an assumption and must be labelled `[A#]`.
3. **Write findings as prose.** "Status chips show the label text with a per-label colour" — not "`.status-chip { background: #00c875 }`".
4. **No screenshots in the repository.** A screenshot is an asset, and assets are the most direct form of copying. If a visual detail must be discussed, describe it in words.
5. **No copying of microcopy.** Paraphrase the function; write our own words.
6. **Never paste their content into a ticket, commit message or design file.**

Rule 3 is the one that gets broken casually, and it is the one that matters: prose describes behaviour, CSS describes expression.

---

## 4. Rules for the implementation team

1. **Work from the behavioural notes**, not from the live competitor product.
2. **Original tokens only** ([04](04-UI-and-UX-Plan.md) §6.3). No hex value may be traced to their palette.
3. **Original iconography and typography.** Licensed fonts with appropriate web licences; icons drawn or licensed, never traced.
4. **Original naming.** Product name, feature names and UI vocabulary are ours. Where an industry term is generic ("board", "item", "timeline"), using it is fine — those are not trademarks.
5. **No compatibility claims.** We do not say "works with", "replaces" or "compatible with" their product in a way that implies endorsement. Factual comparison in marketing is a legal question, not an engineering one.
6. **Behavioural tests assert behaviour.** No pixel-diff against their UI, ever ([04](04-UI-and-UX-Plan.md) §6.2).

---

## 5. Specifically excluded capabilities

These are excluded for legal reasons, not effort. Re-adding any of them requires a recorded decision from [09](09-Risks-and-Open-Questions.md) §5.

| Excluded | Why |
|---|---|
| Import from their export format | Consuming their schema and their users' content shape |
| Their template library | Templates are authored content |
| Their icon set or illustrations | Assets |
| Their colour palette or spacing scale | Trade dress |
| Their terminology where it is distinctive (not generic) | Trademark |
| A "migration from X" wizard | Implies a compatibility relationship and requires their format |

CSV import is included ([06](06-Integrations-and-Extensibility.md) §5). The distinction: a generic CSV with a user-defined column mapping is our format; their export is theirs.

---

## 6. Enforcement

| Control | Where | Fails the build? |
|---|---|---|
| **Banned-string scan** — their trademarks and product names in code, UI strings, docs and metadata | CI, pre-commit | ✅ |
| **Asset-hash scan** — known asset fingerprints in the repository | CI | ✅ |
| **Domain reference scan** — their asset CDNs and doc URLs outside the source register | CI | ✅ |
| **Screenshot scan** — image files in the repo outside `Development-Plans/Diagrams` | CI | ✅ |
| **Licence scan** | CI ([05](05-Security-and-Compliance.md) §9) | ✅ |
| **Provenance review** — every new requirement cites `[S#]`, `[D#]` or `[A#]` | PR review | ✅ (review) |

The banned-string list itself must not live in a public file, because a public list of a competitor's marks is its own small problem. It lives in CI configuration with a comment explaining its purpose.

---

## 7. Provenance log

Every requirement in [01](01-Requirements.md) carries one of:

| Tag | Meaning | Where recorded |
|---|---|---|
| `[S#]` | Derived from a public source | [13-Source-Register.md](13-Source-Register.md) |
| `[D#]` | Owner decision | [00](00-Contents.md) §4 |
| `[A#]` | Our assumption, reversible | [00](00-Contents.md) §4 |

The register records, per source: URL, access date, what it supported, and what it explicitly did **not** support. That last column is what prevents a source being over-claimed later — for example, the pricing page does not document automation semantics, so it cannot be cited for them.

---

## 8. If a problem is found

1. **Stop.** Do not "clean it up later" — later is when it ships.
2. **Quarantine** the offending file or commit.
3. **Record** what it was, where it came from, and how it entered.
4. **Replace** with an original implementation derived from the behavioural note.
5. **Add a control** so the same class of mistake cannot recur (usually a scan pattern).
6. **Note it** in the decision log if it changes scope.

A clean-room claim is only as strong as the weakest file in the repository. One copied stylesheet undoes a year of discipline.

---

## 9. What this protocol cannot guarantee

- It cannot prove a negative. It can show that every behaviour is traceable to a public document and that no protected asset is present.
- It does not replace legal review. Trademark and trade-dress questions are jurisdiction-specific.
- It does not cover an engineer's memory. Someone who has internalised a competitor's CSS may reproduce it from memory; that is why the token layer and the originality reviews exist.

These limits are stated rather than hidden, because a protocol that claims more certainty than it has is worse than one that is honest about its edges.

---

**Next:** [12-Phase-Plan-Summary.md](12-Phase-Plan-Summary.md).
