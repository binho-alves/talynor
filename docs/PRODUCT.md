# Product

## Vision

Talynor is a visual learning and personal knowledge platform combining a personal knowledge graph, learning management, and study tracking. It helps people build a connected understanding of what they learn and see how their learning develops over time.

## Problem

Learning material is often scattered across bookmarks, notes, courses, and files. Folder-based organization makes it difficult to express connections between subjects or identify what to study next. Separate study tools can also disconnect practice and progress from the knowledge they concern.

Talynor brings knowledge organization, learning material, study activity, and practice into one visual experience.

## Target users

- Self-directed learners organizing subjects and resources across multiple sources.
- Students connecting concepts, preparing for assessments, and tracking study activity.
- Professionals developing skills and maintaining a personal body of knowledge.

These are preliminary audiences, not a finalized market segmentation.

## Value proposition

Users can explore how concepts connect, keep resources close to the concepts they explain, and follow their study progress in the same place. Knowledge can be explored at different levels of detail without reducing it to a folder hierarchy.

## Core terminology

| Term | Working meaning |
| --- | --- |
| Atlas / Workspace | Provisional product terms for a user's organized space for interconnected knowledge maps and learning material. Their exact relationship is not yet decided; they are not defined as separate domain entities. |
| Knowledge Graph | The domain concept composed of knowledge nodes and their relationships. |
| Knowledge map | A visual representation or view over knowledge graph concepts. ADR-011 defines the initial relational representation for Phase 4.1; broader product semantics remain open. |
| Knowledge node | A concept, topic, or subject that can have learning content and connections to other nodes. A node may open another map. |
| Relationship / edge | A connection between knowledge nodes. Possible examples include contains, prerequisite, related to, and depends on; these are illustrative, not a final taxonomy. |
| Submap | A map reached by exploring a node at a more detailed knowledge level. |
| Learning content | Material associated with a node, such as an article, external link, video, personal note, file, flashcard, quiz, exercise, or checklist. |
| Study Session | The current working product term for a period of study. Its relationship to finer-grained study activities remains open. |
| Flashcard | A learning item with a prompt/question side and an answer/reveal side. No scheduling or spaced-repetition algorithm is defined. |
| Flashcard Review | The act or result of reviewing a flashcard. |
| Assessment | The initial assessment scope is simple quizzes used to check understanding. Other exercise types may exist later but are outside the currently committed MVP assessment scope. |
| Progress | A view of learning activity and outcomes; exact measures remain to be defined. |

Knowledge relationships must support connections beyond a folder or tree hierarchy. ADR-011 scopes current same-map membership and same-workspace drill-down. Broader cross-map behavior and the exact meaning of each relationship remain open.

## Main user journey

1. Create an account and a knowledge atlas/workspace.
2. Create nodes for subjects of interest and connect them.
3. Explore the map through selection, zoom, pan, and navigation into nodes or submaps.
4. Add learning material to the relevant nodes.
5. Study the material and record study activity.
6. Create and review flashcards, and answer simple quizzes.
7. Review basic progress and return to the map to continue learning.

For example, a Software Development map may connect C#, ASP.NET Core, EF Core, and Architecture. Opening C# may lead to a map containing LINQ, Generics, Async/Await, Nullable, and Collections. Connections may also extend between these topics and other maps.

## Product principles

- Make visual exploration central to the experience.
- Represent connected knowledge beyond folders and trees.
- Let users move between an overview and detailed concepts without losing context.
- Keep learning material and study activity connected to the knowledge they concern.
- Make the interface responsive and understandable across screen sizes.
- Keep the initial experience focused and manageable.
- Treat activity and assessment results as indicators of learning, without assuming that time spent alone demonstrates mastery.

## Preliminary MVP

The MVP should allow a user to:

1. Create an account.
2. Create a knowledge atlas/workspace.
3. Create knowledge nodes.
4. Connect nodes.
5. Navigate into a node/submap.
6. Add learning content.
7. Register study activity.
8. Create flashcards.
9. Review flashcards.
10. Create simple quizzes.
11. View basic progress.

The longer-term tracking direction includes study time, content completion, quiz accuracy, flashcard retention, and learning progress. The exact definitions of these measures and the content types supported in the first release remain open.

## Explicitly outside the initial MVP

- Generative AI, AI tutor, and other AI features.
- Public marketplace and paid learning paths.
- Social networking and real-time collaboration.
- Advanced gamification.
- Native mobile applications.
- Automatic internet importing.
- Recommendation engine.

Technical exclusions are recorded in [Architecture](ARCHITECTURE.md). Listing a possible future content type or capability does not authorize its implementation.
