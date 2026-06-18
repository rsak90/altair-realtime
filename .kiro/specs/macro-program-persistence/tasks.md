# Implementation Plan: Macro Program Persistence

## Overview

This implementation adds session-based macro program persistence to the SAS Job Runner application, completing the session continuity triad alongside macro variables and WORK datasets. The approach follows the proven MacroVarStore pattern: extract macro definitions from SAS catalog after job completion, persist to `macros.sas` file in session folder, and restore via preamble injection on job submission.

## Tasks

- [ ] 1. Create IMacroProgramStore interface and register service
  - Define `IMacroProgramStore` interface in `Services` namespace with `GetAsync` and `SetAsync` methods
  - Create `MacroProgramStore` class implementing the interface
  - Register `MacroProgramStore` as singleton service in `Program.cs` or `Startup.cs`
  - Add IConfiguration and ILogger dependencies to constructor
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 11.1_

- [ ] 2. Implement core MacroProgramStore storage operations
  - [ ] 2.1 Implement internal state management
    - Create concurrent dictionaries for cache (`_cache`), disk load tracking (`_loadedFromDisk`), file locks (`_fileLocks`), and session-to-user mapping (`_sessionToUser`)
    - Read `SessionStorage:StudyFolder` configuration in constructor
    - Implement `RegisterSession(string sessionId, string userId)` method for session-user mapping
    - _Requirements: 6.3, 6.5, 8.6, 8.7, 11.3_
  
  - [ ] 2.2 Implement GetAsync method with lazy loading
    - Check in-memory cache first, return immediately if present
    - If cache miss, check if disk load attempted (prevent repeated failed reads)
    - Call `LoadFromFileAsync` if not previously loaded
    - Update cache with loaded result and return
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 6.1, 6.4, 8.5_
  
  - [ ] 2.3 Implement SetAsync method with fire-and-forget write
    - Update in-memory cache immediately with formatted macro source
    - Queue background file write using `Task.Run` without awaiting
    - Call `FormatMacroSource` to convert dictionary to file content
    - Use `WriteToFileAsync` for actual file I/O
    - _Requirements: 2.1, 6.2, 6.3, 15.1, 15.2_
  
  - [ ]* 2.4 Write unit tests for cache behavior
    - Test cache hit returns immediately without disk access
    - Test cache miss triggers disk load and populates cache
    - Test SetAsync updates cache synchronously
    - Test repeated GetAsync returns cached result
    - _Requirements: 6.1, 6.2, 6.4_

- [ ] 3. Implement file I/O operations
  - [ ] 3.1 Implement LoadFromFileAsync method
    - Construct file path using `GetMacrosFilePath(sessionId, userId)`
    - Acquire per-session file lock using SemaphoreSlim
    - Check if file exists, return empty string if not (log at debug level)
    - Read complete file content using async file I/O
    - Log success at info level with session ID and macro count
    - Handle file I/O exceptions: log warning and return empty string
    - _Requirements: 3.1, 3.2, 3.3, 3.5, 9.2, 9.3, 12.2, 12.4_
  
  - [ ] 3.2 Implement WriteToFileAsync method with atomic writes
    - Construct file path using `GetMacrosFilePath(sessionId, userId)`
    - Create session folder if it doesn't exist (handle directory creation errors)
    - Acquire per-session file lock using SemaphoreSlim
    - Write to temporary file (`.macros.sas.tmp`)
    - Use atomic file rename/move to replace existing `macros.sas`
    - Log success at info level with session ID and macro count
    - Handle file I/O exceptions: log warning, continue with in-memory cache
    - _Requirements: 2.4, 2.5, 7.2, 9.1, 9.4, 12.1, 13.1, 13.2, 13.3_
  
  - [ ] 3.3 Implement helper methods for path resolution
    - Implement `GetMacrosFilePath(sessionId, userId)` returning `{StudyFolder}/sessions/{userId}/{sessionId}/macros.sas`
    - Implement `TryResolveUserId(sessionId)` using `_sessionToUser` dictionary
    - Handle missing StudyFolder configuration: log error, return null
    - Use `Path.Combine` for cross-platform path handling
    - _Requirements: 11.1, 11.2, 11.4, 11.5_
  
  - [ ]* 3.4 Write integration tests for file operations
    - Test successful write creates macros.sas in correct location
    - Test atomic write prevents partial file corruption
    - Test LoadFromFileAsync returns correct content after WriteToFileAsync
    - Test directory creation on first write for new session
    - Test file write failure logs warning and preserves cache
    - _Requirements: 2.1, 2.4, 3.2, 9.1, 13.1_

- [ ] 4. Checkpoint - Ensure storage operations work correctly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Implement macro source formatting and validation
  - [ ] 5.1 Implement FormatMacroSource method
    - Sort macro dictionary keys alphabetically (case-insensitive)
    - Filter out invalid macros using validation logic
    - Generate file header comment with session ID, timestamp, and warning
    - Concatenate each macro with blank line separators
    - Return formatted SAS source code string
    - _Requirements: 2.2, 2.3, 2.6, 10.5, 14.1_
  
  - [ ] 5.2 Implement ValidateMacroSyntax method
    - Check macro name matches pattern `^[A-Z_][A-Z0-9_]*$` (case-insensitive)
    - Check macro name does not start with `SYS_` (case-insensitive)
    - Verify source contains exactly one `%macro <name>` and one matching `%mend <name>;`
    - Check for balanced quotes (', ", `) considering SAS escaping rules
    - Check for balanced parentheses
    - Return validation result with error message if invalid
    - Log warning for invalid macros with macro name and reason
    - _Requirements: 1.7, 10.1, 10.2, 10.3, 10.4, 10.5_
  
  - [ ]* 5.3 Write property tests for formatting and validation
    - **Property 3: Comprehensive Macro Formatting** - Formatted output contains exactly N macro definitions with blank line separators and consistent structure
    - **Property 4: Alphabetical Macro Ordering** - Formatted source lists macros in case-insensitive alphabetical order
    - **Property 6: Macro Name Validation** - Validation accepts all valid SAS identifiers and rejects invalid ones
    - **Property 7: Macro Statement Balance Validation** - Validation accepts only balanced %macro/%mend pairs
    - **Property 8: Quote and Parenthesis Balance Validation** - Validation rejects unbalanced delimiters
    - **Property 9: Invalid Macro Filtering** - Formatted output contains only valid macros
    - _Requirements: 1.7, 2.2, 2.6, 10.1, 10.2, 10.3, 10.5, 14.1_

- [ ] 6. Implement SAS catalog extraction in LogParserService
  - [ ] 6.1 Add macro catalog extraction code generation
    - Create method to generate catalog listing code using PROC CATALOG
    - Generate SQL query to enumerate SASMACR catalog entries from dictionary.catalogs
    - Generate macro extraction loop using %SCAN and %DO WHILE
    - Use %PUT with distinctive markers (`=== MACRO_SOURCE_START: <name> ===` and `=== MACRO_SOURCE_END: <name> ===`)
    - Filter out system macros starting with `SYS_` in extraction loop
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 1.7_
  
  - [ ] 6.2 Implement ParseMacroCatalog method
    - Accept log lines enumerable as input
    - Scan for `=== MACRO_SOURCE_START: <name> ===` markers
    - Collect lines until `=== MACRO_SOURCE_END: <name> ===` marker
    - Build dictionary mapping macro name to extracted source code
    - Handle missing end markers: log warning, skip incomplete macro
    - Handle multi-line macros, special characters, quotes in source
    - Return dictionary of macro name → source code
    - _Requirements: 1.1, 1.2, 1.6, 1.8, 5.5, 5.6_
  
  - [ ]* 6.3 Write property tests for macro extraction
    - **Property 1: System Macro Filtering** - Extraction excludes macros starting with SYS_ (case-insensitive)
    - **Property 2: Macro Extraction Accuracy** - For all macros in log with markers, extracted (name, source) pairs match originals
    - _Requirements: 1.1, 1.2, 1.7, 1.8_
  
  - [ ]* 6.4 Write unit tests for parsing edge cases
    - Test empty log returns empty dictionary
    - Test log with no macro markers returns empty dictionary
    - Test missing end marker logs warning and skips macro
    - Test macros with parameters and local variables extracted correctly
    - Test macros with nested macro calls extracted correctly
    - _Requirements: 1.5, 1.8, 5.5, 5.7_

- [ ] 7. Integrate macro extraction into SessionJobOrchestrator
  - [ ] 7.1 Add catalog extraction code to job postamble
    - Inject catalog extraction code after `%put _user_;` in postamble
    - Ensure extraction code is generated by LogParserService or similar helper
    - Add extraction code only if session persistence is enabled
    - _Requirements: 5.1, 5.4_
  
  - [ ] 7.2 Parse and persist macros after job completion
    - In `StreamAndFinalizeAsync`, after retrieving full log, call `LogParserService.ParseMacroCatalog`
    - Call `MacroProgramStore.RegisterSession(sessionId, userId)` before first macro operation
    - Call `MacroProgramStore.SetAsync(sessionId, macros)` with parsed dictionary
    - Handle parse failures gracefully: log warning, continue without persisting
    - Only persist macros for jobs in terminal states (CompletedSuccess, CompletedError, Failed)
    - _Requirements: 1.1, 1.5, 2.1, 8.7_
  
  - [ ]* 7.3 Write integration tests for orchestrator workflow
    - Test job with macro definition creates macros.sas file
    - Test subsequent job loads and uses persisted macro
    - Test macro redefinition updates macros.sas
    - Test parse failure doesn't break job completion
    - _Requirements: 1.1, 2.1, 15.1, 15.4_

- [ ] 8. Checkpoint - Ensure extraction and persistence pipeline works
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. Extend PreambleBuilder to inject macro programs
  - [ ] 9.1 Update PreambleBuilder.Build method signature
    - Add `string macroPrograms` parameter to Build method
    - Update all callers to pass macro program source from MacroProgramStore
    - _Requirements: 4.1, 8.1_
  
  - [ ] 9.2 Inject macro programs in preamble structure
    - Insert macro program section after library definitions
    - Insert macro program section before macro variable restoration
    - Add comment marker: `/* === MACRO PROGRAM RESTORATION === */`
    - Inject macro program source directly (no additional formatting needed)
    - Omit macro program section if macroPrograms string is empty or whitespace
    - _Requirements: 4.2, 4.3, 4.5_
  
  - [ ]* 9.3 Write property test for preamble ordering
    - **Property 5: Preamble Section Ordering** - Macro program section appears after library definition and before macro variables
    - _Requirements: 4.2_
  
  - [ ]* 9.4 Write unit tests for preamble injection
    - Test preamble with macro programs includes restoration section
    - Test preamble without macro programs omits restoration section
    - Test section ordering: libraries → macros → variables
    - Test macro programs injected only once per preamble
    - _Requirements: 4.2, 4.3, 4.4, 4.5_

- [ ] 10. Update SessionJobOrchestrator to load macros on submission
  - [ ] 10.1 Load macros from MacroProgramStore on job submission
    - In `SubmitAsync`, call `MacroProgramStore.RegisterSession(sessionId, userId)` early
    - Call `MacroProgramStore.GetAsync(sessionId)` to retrieve macro source
    - Pass macro source to PreambleBuilder.Build along with macro variables
    - _Requirements: 3.1, 4.1, 8.7_
  
  - [ ]* 10.2 Write integration test for end-to-end macro persistence
    - Submit job defining macro, verify macro persisted
    - Submit second job using macro, verify macro executes successfully
    - Verify both jobs complete successfully with expected output
    - _Requirements: 3.1, 3.4, 4.1, 4.2_

- [ ] 11. Implement concurrency safety mechanisms
  - [ ] 11.1 Add per-session file locking
    - Use `ConcurrentDictionary<string, SemaphoreSlim>` for per-session file locks
    - Acquire lock before file read or write operations
    - Use try-finally to ensure lock release
    - _Requirements: 7.2, 7.4_
  
  - [ ] 11.2 Implement atomic file write pattern
    - Write to temporary file with `.tmp` extension
    - Use File.Move with overwrite=true to atomically replace existing file
    - Delete temp file if move fails
    - _Requirements: 2.4, 7.2_
  
  - [ ]* 11.3 Write concurrency tests
    - Test concurrent SetAsync calls for same session update cache correctly
    - Test concurrent file writes don't corrupt macros.sas
    - Test cache updates are atomic across threads
    - _Requirements: 7.1, 7.2, 7.4_

- [ ] 12. Implement structured logging throughout
  - [ ] 12.1 Add logging to MacroProgramStore operations
    - Log info on successful file write with sessionId, userId, macro count
    - Log info on successful file load with sessionId, macro count
    - Log debug on cache hit with sessionId
    - Log debug on new session (file doesn't exist)
    - Log warning on file I/O errors with sessionId, filePath, error details
    - Log warning on validation errors with sessionId, macroName, validation error
    - Use structured logging properties: sessionId, userId, filePath, macroCount
    - _Requirements: 9.5, 10.4, 12.1, 12.2, 12.3, 12.4, 12.5_
  
  - [ ] 12.2 Add logging to LogParserService
    - Log warning when catalog extraction fails with error details
    - Log warning when macro parsing encounters incomplete markers
    - Log debug with macro count when extraction succeeds
    - _Requirements: 1.5, 5.7, 12.6_

- [ ] 13. Add error handling and graceful degradation
  - [ ] 13.1 Implement fallback for missing configuration
    - Check if StudyFolder configuration is null or empty in constructor
    - Log error at startup if configuration missing
    - Set internal flag to operate in memory-only mode
    - Return empty string from GetAsync if in memory-only mode
    - Skip file writes in SetAsync if in memory-only mode
    - _Requirements: 9.3, 9.4, 11.5_
  
  - [ ] 13.2 Handle partial macro persistence failures
    - Continue processing remaining macros if validation fails for one
    - Continue job completion if macro extraction fails
    - Preserve in-memory cache state when file write fails
    - _Requirements: 1.5, 9.1, 9.3, 10.5_
  
  - [ ]* 13.3 Write error handling tests
    - Test missing StudyFolder config operates in memory-only mode
    - Test file write failure preserves cache and logs warning
    - Test file read failure returns cached state
    - Test validation failure for one macro doesn't affect others
    - _Requirements: 9.1, 9.2, 9.3, 10.4, 10.5_

- [ ] 14. Implement round-trip preservation properties
  - [ ]* 14.1 Write property tests for round-trip preservation
    - **Property 10: Round-Trip Preservation** - Format → Write → Read produces functionally equivalent macro definitions
    - Test preservation of macro parameters
    - Test preservation of local variables
    - Test preservation of comments (best-effort)
    - Test preservation of macro body logic (structural equivalence)
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

- [ ] 15. Implement macro update and deduplication logic
  - [ ]* 15.1 Write property tests for update behavior
    - **Property 11: Macro Update Deduplication** - Persisted file contains only most recent definition per macro name
    - **Property 12: Single Macro Update Isolation** - Updating one macro doesn't affect others in cache
    - _Requirements: 15.1, 15.2, 15.3, 15.5_
  
  - [ ]* 15.2 Write unit tests for macro update scenarios
    - Test redefinition replaces previous definition in cache
    - Test redefinition updates macros.sas with latest version
    - Test update logs debug message
    - Test other macros remain unchanged after single update
    - _Requirements: 15.1, 15.2, 15.4, 15.5_

- [ ] 16. Final checkpoint and integration validation
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test tasks and can be skipped for faster MVP implementation
- Each task references specific requirements using requirement numbers (e.g., 1.1, 2.3)
- Property-based tests validate universal correctness properties from the design document
- Integration tests verify end-to-end workflows with real file system and SAS catalog interaction
- The implementation follows the MacroVarStore architecture pattern throughout
- Checkpoints ensure incremental validation at key integration points
- All file I/O operations include error handling and graceful degradation
- Structured logging enables debugging of session persistence issues

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1"] },
    { "id": 1, "tasks": ["2.1", "5.1", "5.2"] },
    { "id": 2, "tasks": ["2.2", "2.3", "3.3", "6.1"] },
    { "id": 3, "tasks": ["2.4", "3.1", "3.2", "5.3", "6.2"] },
    { "id": 4, "tasks": ["3.4", "6.3", "6.4", "7.1"] },
    { "id": 5, "tasks": ["7.2", "9.1"] },
    { "id": 6, "tasks": ["7.3", "9.2", "11.1", "11.2"] },
    { "id": 7, "tasks": ["9.3", "9.4", "10.1", "12.1", "12.2"] },
    { "id": 8, "tasks": ["10.2", "11.3", "13.1", "13.2"] },
    { "id": 9, "tasks": ["13.3", "14.1", "15.1", "15.2"] }
  ]
}
```
