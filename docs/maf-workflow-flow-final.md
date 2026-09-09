# MAF Topic-Group Workflow

`TopicGroupWorkflow` runs one checkpointed MAF workflow per topic group.

```mermaid
flowchart TD
    START["PassStart<br/>start one topic-group workflow"] --> QS

    subgraph PIPELINE["MAF Executor Graph (one executor per super-step)"]
        direction TB
        QS["1 - QuerySynthesisExecutor<br/>create one query from keywords and history<br/>start and record the pass"]
        WS["2 - WebSearchExecutor<br/>run a versioned Foundry agent definition<br/>through MAF AIAgent<br/>map Bing-grounded URL citations to hits"]
        PF["3 - PreFilterExecutor<br/>canonicalize and validate URLs<br/>de-duplicate within and across groups<br/>record retained hits"]
        FC["4 - FetchAndCleanExecutor<br/>fetch and clean each document<br/>mark snippet fallback as unverified"]
        RE["5 - RelevanceEvalExecutor<br/>if documents exist, evaluate them in one call<br/>return item verdicts and a loop decision"]
        LC["6 - LoopControllerExecutor<br/>apply loop cap and recall override<br/>discard confidently out-of-window items<br/>record review and snapshot available vetted text"]

        QS -->|QueryResult| WS
        WS -->|HitsResult| PF
        PF -->|FilteredHitsResult| FC
        FC -->|DocumentsResult| RE
        RE -->|EvaluationResult| LC
    end

    LC --> BRANCH{"Review.FinalDecision"}
    BRANCH -->|Retry| QS
    BRANCH -->|Finalize| FIN

    FIN["7 - FinalizeExecutor<br/>collect vetted items from all passes<br/>enrich and classify each document<br/>generate each Company View"]
    FIN -->|YieldOutputAsync| OUT["TopicGroupResult<br/>status, items, and pass history"]

    subgraph CKPT["Checkpointing to Azure Cosmos DB"]
        direction TB
        MGR["MAF CheckpointManager<br/>checkpoint after each super-step"]
        STATE["Shared SearchHistory state<br/>QuerySynthesisExecutor save/restore hooks"]
        STORE["CosmosCheckpointStore<br/>JsonCheckpointStore implementation"]
        DOC["CheckpointDocument<br/>id, sessionId (PK), checkpointId<br/>parentCheckpointId, valueJson"]
        CDB["Cosmos DB<br/>container: checkpoints<br/>partition: /sessionId"]
        STATE --> MGR --> STORE --> DOC --> CDB
    end

    QS -. queue shared state .-> STATE
    STATE -. ResumeStreamingAsync<br/>when supplied a checkpoint .-> QS

    classDef exec fill:#e1f5ff,stroke:#0078D4,color:#172B4D;
    classDef term fill:#c8e6c9,stroke:#107C10,color:#172B4D;
    classDef cosmos fill:#ffe0b2,stroke:#ff6f00,color:#172B4D;
    classDef branch fill:#fff9c4,stroke:#c9a400,color:#172B4D;

    class QS,WS,PF,FC,RE,LC,FIN exec;
    class START,OUT term;
    class MGR,STATE,STORE,DOC,CDB cosmos;
    class BRANCH branch;
```

## Notes

- **Execution**: `ScanOrchestrator` runs topic-group workflows concurrently, limited by `MaxParallelTopicGroups`. Each workflow processes its executors sequentially.
- **Web search**: the provisioning CLI creates a versioned `DeclarativeAgentDefinition` containing the Foundry model deployment and server-side Grounding with Bing Custom Search tool. Runtime resolves the definition and adapts it to MAF with `AIProjectClient.AsAIAgent(...)`; `WebSearchAgent` streams the run and maps citations to hits. This is not a Foundry Hosted Agent workload.
- **Loop policy**: relevance evaluation supplies `Retry` or `Finalize`. The controller always finalizes at `MaxLoops`; below the cap, it changes `Finalize` to `Retry` when at least 80% of verdicts are `Relevant`.
- **Finalization**: the controller records vetted and discarded items per pass. `FinalizeExecutor` collects vetted items across all passes, reads available Blob snapshots, enriches each item, assigns per-document impact area and tags, generates per-document Company Views, and yields the result with pass history.
- **Checkpointing**: production persists a checkpoint after each super-step. `QuerySynthesisExecutor` alone queues the shared `SearchHistory`; `CosmosCheckpointStore` upserts each checkpoint under its `sessionId`. The workflow can restore an empty history through `ResumeStreamingAsync`, but the production executor currently always starts with `RunStreamingAsync` and does not select a stored checkpoint.
