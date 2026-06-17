# Requirements Document

## Introduction

This feature implements session-based macro variable persistence in the SAS Job Runner application. Currently, macro variables are stored in-memory using a ConcurrentDictionary, which means they persist across job submissions within an application session but are lost on application restart. This feature adds file-based persistence to a `variables.json` file stored in each session's folder, enabling macro variables to survive application restarts and providing the foundation for true session continuity.

This is the first step toward making separate SAS job submissions behave as if they're running in the same continuous SAS session, alongside the already-implemented WORK dataset persistence.

## Glossary

- **Macro_Var_Store**: The service implementing IMacroVarStore interface, responsible for managing macro variable storage and retrieval
- **Session_Folder**: The directory path `{StudyFolder}/sessions/{userId}/{sessionId}/` where session-specific files are stored
- **Variables_File**: The JSON file at `{Session_Folder}/variables.json` containing persisted macro variables
- **Macro_Variable**: A SAS macro variable, stored as a name-value pair (both strings)
- **Job_Orchestrator**: The SessionJobOrchestrator service that coordinates job submission, execution, and macro variable updates
- **In_Memory_Cache**: The ConcurrentDictionary used for fast access to macro variables during runtime

## Requirements

### Requirement 1: Persist Macro Variables to File After Job Completion

**User Story:** As a SAS user, I want my macro variables to be saved to disk after each job completes, so that they survive application restarts and I can continue my session seamlessly.

#### Acceptance Criteria

1. WHEN Job_Orchestrator updates macro variables after parsing job logs, THE Macro_Var_Store SHALL write the complete set of macro variables to Variables_File
2. THE Macro_Var_Store SHALL serialize the macro variables as a JSON object with string keys and string values
3. THE Macro_Var_Store SHALL write Variables_File atomically to prevent partial writes during concurrent access
4. IF the Session_Folder does not exist when writing Variables_File, THEN THE Macro_Var_Store SHALL create the directory structure
5. THE Macro_Var_Store SHALL log a warning and continue operation if file write fails, preserving in-memory state

### Requirement 2: Load Macro Variables from File on Retrieval

**User Story:** As a SAS user, I want my macro variables to be loaded from disk when I submit a new job, so that I can access variables created in previous jobs even after application restart.

#### Acceptance Criteria

1. WHEN Job_Orchestrator requests macro variables for a session, THE Macro_Var_Store SHALL check if Variables_File exists in Session_Folder
2. IF Variables_File exists, THEN THE Macro_Var_Store SHALL read and deserialize the JSON content into macro variable dictionary
3. IF Variables_File does not exist, THEN THE Macro_Var_Store SHALL return an empty dictionary
4. THE Macro_Var_Store SHALL populate In_Memory_Cache with loaded macro variables for subsequent fast access
5. IF file read or deserialization fails, THEN THE Macro_Var_Store SHALL log a warning and return the current In_Memory_Cache state

### Requirement 3: Maintain In-Memory Cache for Performance

**User Story:** As a system administrator, I want macro variable access to remain fast during job execution, so that the persistence layer doesn't slow down job submission.

#### Acceptance Criteria

1. WHEN macro variables are retrieved for a session, THE Macro_Var_Store SHALL return values from In_Memory_Cache if present
2. WHEN macro variables are updated, THE Macro_Var_Store SHALL update In_Memory_Cache immediately
3. THE Macro_Var_Store SHALL use In_Memory_Cache as the source of truth during runtime
4. THE Macro_Var_Store SHALL only access Variables_File when In_Memory_Cache is empty for a given session
5. THE Macro_Var_Store SHALL maintain thread-safety using ConcurrentDictionary for In_Memory_Cache

### Requirement 4: Handle Concurrent Access Safely

**User Story:** As a system administrator, I want the persistence layer to handle concurrent job submissions safely, so that macro variables are not corrupted when multiple jobs run simultaneously for the same session.

#### Acceptance Criteria

1. WHEN multiple threads update macro variables for the same session concurrently, THE Macro_Var_Store SHALL ensure In_Memory_Cache updates are atomic
2. WHEN writing Variables_File, THE Macro_Var_Store SHALL use file system locking or atomic write operations to prevent corruption
3. IF a file write operation is in progress, THEN THE Macro_Var_Store SHALL queue subsequent writes or use the latest in-memory state
4. THE Macro_Var_Store SHALL preserve the thread-safety guarantees of the existing ConcurrentDictionary implementation
5. THE Macro_Var_Store SHALL not introduce race conditions between file I/O and in-memory operations

### Requirement 5: Maintain Backward Compatibility

**User Story:** As a developer, I want the persistence feature to work with existing code without requiring API changes, so that I don't need to refactor consumers of IMacroVarStore.

#### Acceptance Criteria

1. THE Macro_Var_Store SHALL implement the existing IMacroVarStore interface without modifications
2. THE Macro_Var_Store SHALL accept the same parameters for GetAsync, SetAsync, and SetVarAsync methods
3. THE Macro_Var_Store SHALL return the same data types and structures as the current implementation
4. THE Macro_Var_Store SHALL remain registered as a singleton service in dependency injection
5. WHERE persistence fails, THE Macro_Var_Store SHALL fall back to in-memory-only behavior transparently

### Requirement 6: Handle File System Errors Gracefully

**User Story:** As a system administrator, I want the application to continue working even if file system errors occur, so that temporary I/O issues don't break active SAS sessions.

#### Acceptance Criteria

1. IF Variables_File cannot be written due to permissions, disk space, or I/O errors, THEN THE Macro_Var_Store SHALL log the error at warning level
2. IF Variables_File cannot be read due to file corruption or format errors, THEN THE Macro_Var_Store SHALL log the error and return an empty dictionary
3. THE Macro_Var_Store SHALL continue serving macro variables from In_Memory_Cache when file operations fail
4. THE Macro_Var_Store SHALL not throw exceptions that bubble up to Job_Orchestrator during normal operation
5. WHEN file operations fail, THE Macro_Var_Store SHALL include session ID and file path in log messages for debugging

### Requirement 7: Validate JSON Structure on Read

**User Story:** As a developer, I want the persistence layer to validate JSON structure when loading variables, so that corrupted or manually-edited files don't crash the application.

#### Acceptance Criteria

1. WHEN deserializing Variables_File, THE Macro_Var_Store SHALL verify the root element is a JSON object
2. THE Macro_Var_Store SHALL verify all keys in the JSON object are strings
3. THE Macro_Var_Store SHALL verify all values in the JSON object are strings
4. IF the JSON structure is invalid, THEN THE Macro_Var_Store SHALL log a warning with details and return an empty dictionary
5. THE Macro_Var_Store SHALL ignore JSON keys that are not valid SAS macro variable names

### Requirement 8: Support Session Folder Path Resolution

**User Story:** As a system administrator, I want the persistence layer to correctly resolve session folder paths, so that variables are stored in the correct location per user and session.

#### Acceptance Criteria

1. THE Macro_Var_Store SHALL require IConfiguration dependency to access SessionStorage:StudyFolder configuration
2. THE Macro_Var_Store SHALL construct Session_Folder path as `{StudyFolder}/sessions/{userId}/{sessionId}/`
3. THE Macro_Var_Store SHALL store sessionId-to-userId mapping or accept userId as parameter to SetAsync and GetAsync methods
4. THE Macro_Var_Store SHALL handle path separators correctly on Windows and Unix systems
5. IF SessionStorage:StudyFolder configuration is missing, THEN THE Macro_Var_Store SHALL log an error and fall back to in-memory-only mode

### Requirement 9: Provide Observable Logging for Debugging

**User Story:** As a developer debugging session issues, I want to see when macro variables are loaded from or saved to disk, so that I can troubleshoot persistence problems.

#### Acceptance Criteria

1. WHEN Variables_File is successfully written, THE Macro_Var_Store SHALL log at information level with session ID and variable count
2. WHEN Variables_File is successfully loaded, THE Macro_Var_Store SHALL log at information level with session ID and variable count
3. WHEN file operations fail, THE Macro_Var_Store SHALL log at warning level with session ID, operation type, and error details
4. WHEN Variables_File does not exist on first read, THE Macro_Var_Store SHALL log at debug level indicating new session
5. THE Macro_Var_Store SHALL use structured logging with sessionId, userId, and filePath as properties

### Requirement 10: Initialize Session Storage on First Access

**User Story:** As a SAS user starting a new session, I want the system to automatically create my session folder structure, so that I don't encounter errors when the first job saves macro variables.

#### Acceptance Criteria

1. WHEN writing Variables_File for a new session, THE Macro_Var_Store SHALL check if Session_Folder exists
2. IF Session_Folder does not exist, THEN THE Macro_Var_Store SHALL create the full directory path including parent directories
3. THE Macro_Var_Store SHALL handle directory creation failures gracefully and log warnings
4. THE Macro_Var_Store SHALL not fail the macro variable update operation if directory creation succeeds but file write fails
5. WHERE Session_Folder already exists, THE Macro_Var_Store SHALL not attempt directory creation
