# Implementation Plan: Session Macro Persistence

## Overview

This plan implements file-based persistence for SAS macro variables in the SAS Job Runner application. The implementation extends the existing `MacroVarStore` service to write macro variables to a `variables.json` file in each session's folder after job completion, and to load them on first access after application restart. The solution maintains the existing high-performance in-memory cache while adding a transparent persistence layer that handles concurrent access safely and degrades gracefully on file system errors.

## Tasks

- [x] 1. Create internal data models for file serialization
  - Create `MacroVarFile` class with `Metadata` and `Variables` properties
  - Create `MacroVarMetadata` class with `UserId` and `LastUpdated` properties
  - Ensure models support JSON serialization with System.Text.Json
  - _Requirements: 1.2, 2.2, 8.2_

- [x] 2. Enhance MacroVarStore with persistence infrastructure
  - [x] 2.1 Add private fields for tracking and locking
    - Add `_loadedFromDisk` ConcurrentDictionary to track which sessions have been loaded
    - Add `_fileLocks` ConcurrentDictionary to store per-session SemaphoreSlim instances
    - Add `_sessionToUser` ConcurrentDictionary to map sessionId to userId
    - Add `IConfiguration` and `ILogger<MacroVarStore>` dependencies
    - Add `_studyFolder` field to cache configuration value
    - _Requirements: 3.4, 4.2, 8.1, 8.2_

  - [x] 2.2 Update constructor to inject dependencies and validate configuration
    - Inject `IConfiguration` and `ILogger<MacroVarStore>` in constructor
    - Read `SessionStorage:StudyFolder` configuration and store in `_studyFolder`
    - Log error and warn about in-memory-only fallback if StudyFolder is missing
    - Log information about successful initialization with StudyFolder path
    - _Requirements: 8.1, 8.5, 9.2_

  - [ ]* 2.3 Write unit tests for constructor and configuration handling
    - Test successful initialization with valid StudyFolder configuration
    - Test graceful degradation when StudyFolder is missing
    - Verify appropriate log messages at correct levels
    - _Requirements: 8.5, 9.2_

- [x] 3. Implement file path construction and userId resolution
  - [x] 3.1 Create GetVariablesFilePath method
    - Accept sessionId and userId parameters
    - Return path: `{StudyFolder}/sessions/{userId}/{sessionId}/variables.json`
    - Use Path.Combine for cross-platform compatibility
    - Throw InvalidOperationException if StudyFolder is null/empty
    - _Requirements: 8.2, 8.4_

  - [x] 3.2 Create TryResolveUserId method
    - Check `_sessionToUser` cache first for sessionId mapping
    - If not cached, scan `{StudyFolder}/sessions/*/` directories to find sessionId
    - Cache discovered userId in `_sessionToUser` for future use
    - Return null if userId cannot be resolved
    - Handle directory access errors gracefully with logging
    - _Requirements: 8.2, 8.3, 6.4_

  - [ ]* 3.3 Write property test for path construction
    - **Property 8: Path construction follows session folder pattern**
    - **Validates: Requirements 8.2, 8.4**
    - Verify path format matches `{StudyFolder}/sessions/{userId}/{sessionId}/variables.json`
    - Test with various valid userId and sessionId combinations
    - Verify correct path separators for Windows

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement file read operations with lazy loading
  - [x] 5.1 Create LoadFromFileAsync private method
    - Accept sessionId parameter
    - Use TryResolveUserId to get userId for path construction
    - Check if variables.json file exists at constructed path
    - If file exists, read content and deserialize to MacroVarFile
    - Validate JSON structure (object with string keys and values)
    - Extract variables dictionary and userId from metadata
    - Cache userId in `_sessionToUser` for future use
    - Return variables dictionary or empty dictionary on any failure
    - Log at debug level for missing files, warning level for errors
    - _Requirements: 2.1, 2.2, 2.3, 6.2, 7.1-7.5, 9.2, 9.4_

  - [x] 5.2 Update GetAsync method to implement lazy loading
    - Check if session exists in `_store` cache - if yes, return immediately
    - Check if session is marked as loaded in `_loadedFromDisk` - if yes, return empty
    - If not loaded, call LoadFromFileAsync to read from disk
    - Populate `_store` cache with loaded variables
    - Mark session as loaded in `_loadedFromDisk` to prevent repeated reads
    - Return cached variables as IReadOnlyDictionary
    - _Requirements: 2.4, 3.1, 3.3, 3.4_

  - [ ]* 5.3 Write property test for lazy loading behavior
    - **Property 2: Cache loading is lazy and consistent**
    - **Validates: Requirements 2.4, 3.4**
    - Verify first GetAsync call loads from file
    - Verify subsequent GetAsync calls return cached values without file I/O
    - Test with sessions that have persisted variables

  - [ ]* 5.4 Write property test for cache-first retrieval
    - **Property 3: Cache-first retrieval bypasses file I/O**
    - **Validates: Requirements 3.1**
    - Populate cache with variables using SetAsync
    - Call GetAsync multiple times and verify no file access occurs
    - Use file I/O monitoring to verify zero file reads

- [x] 6. Implement file write operations with atomic writes
  - [x] 6.1 Create GetFileLock helper method
    - Accept sessionId parameter
    - Return SemaphoreSlim from `_fileLocks` dictionary (create if not exists)
    - Use GetOrAdd for thread-safe initialization
    - _Requirements: 4.2, 4.3_

  - [x] 6.2 Create WriteToFileAsync private method
    - Accept sessionId, userId, and variables dictionary parameters
    - Acquire file lock using GetFileLock and SemaphoreSlim.WaitAsync
    - Construct MacroVarFile with metadata (userId, DateTime.UtcNow) and variables
    - Serialize to JSON using JsonSerializer with indented formatting
    - Ensure session directory exists (create if needed with Directory.CreateDirectory)
    - Write to temporary file: `variables.{Guid.NewGuid()}.tmp`
    - Use File.Move with overwrite:true to atomically rename to variables.json
    - Log success at information level with sessionId, userId, and variable count
    - Wrap all operations in try-catch, log warnings on errors, continue operation
    - Always release file lock in finally block
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 4.2, 6.1, 6.4, 6.5, 9.1, 10.1-10.5_

  - [x] 6.3 Update SetAsync method to trigger file write
    - Update `_store[sessionId]` with new dictionary immediately (synchronous)
    - Cache userId from `_sessionToUser` or extract from context
    - Fire-and-forget call to WriteToFileAsync in Task.Run
    - Return Task.CompletedTask immediately (don't await file write)
    - _Requirements: 1.1, 3.2, 4.1_

  - [x] 6.4 Update SetVarAsync method to trigger file write
    - Use `_store.AddOrUpdate` to atomically update single variable
    - Fire-and-forget call to WriteToFileAsync with complete variable set
    - Return Task.CompletedTask immediately
    - _Requirements: 3.2, 4.1_

  - [ ]* 6.5 Write property test for immediate cache updates
    - **Property 4: Cache updates are immediate and synchronous**
    - **Validates: Requirements 3.2**
    - Call SetAsync or SetVarAsync with new values
    - Immediately call GetAsync and verify updated values are returned
    - Verify no waiting for file operations

  - [ ]* 6.6 Write property test for serialization round-trip
    - **Property 1: Serialization round-trip preserves data**
    - **Validates: Requirements 1.2, 2.2**
    - Generate random dictionaries with string keys and values
    - Serialize to MacroVarFile JSON and deserialize back
    - Verify all key-value pairs are preserved exactly

- [ ] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement error handling and validation
  - [x] 8.1 Add JSON validation in LoadFromFileAsync
    - Verify root element is object type (not array or primitive)
    - Verify metadata section exists with userId string
    - Verify variables section is dictionary with string keys and values
    - Skip invalid entries and continue with valid ones
    - Log warnings with details for validation failures
    - _Requirements: 7.1-7.5_

  - [x] 8.2 Add comprehensive error logging in WriteToFileAsync
    - Catch IOException, UnauthorizedAccessException, and general exceptions separately
    - Include sessionId, userId, filePath, and error message in all log entries
    - Use structured logging properties for easy filtering
    - _Requirements: 6.4, 6.5, 9.1, 9.3_

  - [x] 8.3 Add comprehensive error logging in LoadFromFileAsync
    - Distinguish between FileNotFoundException (debug), JsonException (warning), and other errors (warning)
    - Include sessionId, userId, filePath in structured log properties
    - _Requirements: 6.2, 9.2, 9.3, 9.4_

  - [ ]* 8.4 Write property test for JSON validation
    - **Property 7: JSON validation rejects invalid structure**
    - **Validates: Requirements 7.1-7.5**
    - Generate invalid JSON structures (non-object root, non-string keys/values)
    - Verify deserialization fails gracefully with empty dictionary
    - Verify appropriate warnings are logged

  - [ ]* 8.5 Write property test for fault tolerance
    - **Property 6: File system failures are transparent and non-fatal**
    - **Validates: Requirements 5.5, 6.1-6.4**
    - Simulate file read/write failures using mock file system
    - Verify MacroVarStore continues operating with in-memory cache
    - Verify no exceptions propagate to callers
    - Verify appropriate errors are logged

- [x] 9. Implement userId tracking for path construction
  - [x] 9.1 Update SetAsync to capture and cache userId
    - When SetAsync is called, attempt to resolve userId from context or parameters
    - Store sessionId-to-userId mapping in `_sessionToUser` dictionary
    - If userId cannot be determined, log warning and proceed with file write skipped
    - _Requirements: 8.3_

  - [x] 9.2 Update SessionJobOrchestrator to pass userId context
    - Add internal method RegisterSession(sessionId, userId) to MacroVarStore
    - Call RegisterSession from SessionJobOrchestrator.SubmitAsync before GetAsync
    - Store userId in `_sessionToUser` mapping for immediate availability
    - _Requirements: 8.3_

  - [ ]* 9.3 Write integration test for userId resolution
    - Create session with userId through SessionJobOrchestrator flow
    - Verify userId is correctly tracked in `_sessionToUser`
    - Verify file is written to correct path with userId
    - Restart MacroVarStore (clear cache) and verify userId can be resolved from file system

- [ ] 10. Implement concurrent access tests
  - [ ]* 10.1 Write integration test for concurrent writes
    - Submit 10 concurrent SetAsync operations for same sessionId with different values
    - Verify variables.json file is valid JSON after all writes complete
    - Verify file contains one of the written value sets (last-write-wins)
    - Verify no file corruption occurs
    - _Requirements: 1.3, 4.1, 4.2_

  - [ ]* 10.2 Write property test for concurrent cache consistency
    - **Property 5: Concurrent updates maintain cache consistency**
    - **Validates: Requirements 4.1**
    - Generate sequence of concurrent SetAsync operations
    - Verify final cache state is consistent and matches one operation's values
    - Verify no cache corruption or partial updates

- [x] 11. Update dependency injection registration
  - Verify MacroVarStore is still registered as singleton in Program.cs or Startup.cs
  - Ensure IConfiguration is available in DI container (should already exist)
  - No changes needed to service registration (backward compatible)
  - _Requirements: 5.1, 5.4_

- [x] 12. Add structured logging throughout implementation
  - [x] 12.1 Add structured logging to WriteToFileAsync
    - Log at information level on successful write with sessionId, userId, variableCount, durationMs
    - Log at warning level on write failures with sessionId, userId, filePath, errorMessage
    - Use structured properties for all log data (not string interpolation)
    - _Requirements: 9.1, 9.3_

  - [x] 12.2 Add structured logging to LoadFromFileAsync
    - Log at debug level when file doesn't exist with sessionId
    - Log at information level on successful load with sessionId, userId, variableCount
    - Log at warning level on read/parse failures with sessionId, userId, filePath, errorMessage
    - _Requirements: 9.2, 9.4, 9.5_

  - [x] 12.3 Add structured logging to constructor
    - Log at error level if StudyFolder is missing
    - Log at information level on successful initialization with studyFolder path
    - _Requirements: 9.5_

- [-] 13. Final integration and verification
  - [ ] 13.1 Create end-to-end integration test
    - Submit job through SessionJobOrchestrator with macro variables
    - Verify variables are written to correct file path
    - Clear MacroVarStore cache (simulate restart)
    - Submit new job and verify variables are loaded from file
    - Verify loaded variables are used in subsequent job submissions
    - _Requirements: 1.1-1.5, 2.1-2.5, 8.2_

  - [ ] 13.2 Test backward compatibility
    - Verify existing code using IMacroVarStore works without changes
    - Verify API signatures remain unchanged
    - Verify singleton service registration works as before
    - _Requirements: 5.1-5.5_

  - [ ] 13.3 Test graceful degradation
    - Test with missing StudyFolder configuration - verify in-memory-only fallback
    - Test with read-only file system - verify warnings logged, cache still works
    - Test with corrupted variables.json file - verify empty dictionary returned
    - _Requirements: 5.5, 6.1-6.5, 8.5_

- [ ] 14. Final checkpoint - Ensure all tests pass
  - Run all unit tests, property tests, and integration tests
  - Verify no regressions in existing functionality
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP delivery
- Property-based tests use CsCheck library with minimum 100 iterations per property
- Integration tests use actual file system with temporary test directories
- File operations use atomic write pattern (temp file + rename) to prevent corruption
- Per-session locking prevents concurrent writes from corrupting the same file
- Cache updates are synchronous and immediate - file writes are fire-and-forget async
- The userId tracking challenge is solved by storing userId in file metadata and caching the mapping
- All file I/O errors are caught, logged, and do not propagate to callers (fail-safe design)
- Backward compatibility is maintained - no changes to IMacroVarStore interface

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "3.1", "3.2"] },
    { "id": 3, "tasks": ["3.3", "5.1"] },
    { "id": 4, "tasks": ["5.2"] },
    { "id": 5, "tasks": ["5.3", "5.4", "6.1"] },
    { "id": 6, "tasks": ["6.2"] },
    { "id": 7, "tasks": ["6.3", "6.4"] },
    { "id": 8, "tasks": ["6.5", "6.6", "8.1", "8.2", "8.3"] },
    { "id": 9, "tasks": ["8.4", "8.5", "9.1"] },
    { "id": 10, "tasks": ["9.2"] },
    { "id": 11, "tasks": ["9.3", "10.1", "10.2", "11.1", "12.1", "12.2", "12.3"] },
    { "id": 12, "tasks": ["13.1", "13.2", "13.3"] }
  ]
}
```
