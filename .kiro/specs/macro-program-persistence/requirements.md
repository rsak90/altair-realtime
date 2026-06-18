# Requirements Document

## Introduction

This feature implements session-based macro program persistence in the SAS Job Runner application. Macro programs are SAS macro definitions created using `%macro/%mend` statements (e.g., `%macro greet(name); %put Hello, &name!; %mend greet;`). Currently, macro programs defined during a job execution are lost after the job completes, requiring users to redefine them in each submission.

This feature extends the existing session continuity pattern (already implemented for macro variables in `variables.json` and WORK datasets in `.sas7bdat` files) to include macro program definitions. When users define macros in their SAS code, those macro definitions will be saved to disk after each job completes and automatically loaded via preamble code when new jobs are submitted.

This makes separate job submissions behave as if they're running in a continuous SAS session, completing the session persistence triad: datasets, macro variables, and macro programs.

## Glossary

- **Macro_Program**: A SAS macro definition created using `%macro/%mend` statements, consisting of a macro name and its source code
- **Macro_Program_Store**: The service implementing IMacroProgramStore interface, responsible for managing macro program storage and retrieval
- **Session_Folder**: The directory path `{StudyFolder}/sessions/{userId}/{sessionId}/` where session-specific files are stored
- **Macros_File**: The SAS source file at `{Session_Folder}/macros.sas` containing persisted macro program definitions
- **Job_Orchestrator**: The SessionJobOrchestrator service that coordinates job submission, execution, and macro program updates
- **Preamble_Builder**: The PreambleBuilder service that constructs preamble code injected before user code execution
- **SASMACR_Catalog**: The SAS catalog `WORK.SASMACR` where compiled macro programs are stored during SAS session execution
- **Log_Parser**: The LogParserService that extracts information from SAS job logs
- **In_Memory_Cache**: A concurrent dictionary used for fast access to macro program definitions during runtime

## Requirements

### Requirement 1: Parse Macro Program Definitions from SAS Logs

**User Story:** As a SAS user, I want the system to detect when I define macro programs in my code, so that those macros are automatically saved for future use.

#### Acceptance Criteria

1. WHEN a SAS job completes, THE Log_Parser SHALL examine the job log output to identify macro program definitions
2. THE Log_Parser SHALL extract macro program names and their complete source code from the log
3. THE Log_Parser SHALL use PROC CATALOG or equivalent SAS procedures to list macros stored in SASMACR_Catalog
4. THE Log_Parser SHALL extract the source code for each macro using `%COPY` statement or catalog source extraction
5. IF macro source extraction fails for a specific macro, THEN THE Log_Parser SHALL log a warning and continue processing remaining macros
6. THE Log_Parser SHALL return a dictionary mapping macro names (as keys) to macro source code (as values)
7. THE Log_Parser SHALL ignore SAS system macros (e.g., macros starting with `SYS_` or `%SYSFUNC`)
8. THE Log_Parser SHALL handle macros with parameters, local variables, and nested macro calls

### Requirement 2: Persist Macro Programs to File After Job Completion

**User Story:** As a SAS user, I want my macro program definitions to be saved to disk after each job completes, so that they survive application restarts and I can use them in subsequent jobs.

#### Acceptance Criteria

1. WHEN Job_Orchestrator completes macro program parsing, THE Macro_Program_Store SHALL write all macro programs to Macros_File
2. THE Macro_Program_Store SHALL format Macros_File as valid SAS source code with each macro definition separated by blank lines
3. THE Macro_Program_Store SHALL include a header comment in Macros_File indicating it is auto-generated with a timestamp
4. THE Macro_Program_Store SHALL write Macros_File atomically to prevent partial writes during concurrent access
5. IF the Session_Folder does not exist when writing Macros_File, THEN THE Macro_Program_Store SHALL create the directory structure
6. THE Macro_Program_Store SHALL preserve macro definition order based on alphabetical macro name sorting
7. THE Macro_Program_Store SHALL log a warning and continue operation if file write fails, preserving in-memory state

### Requirement 3: Load Macro Programs from File on Session Start

**User Story:** As a SAS user, I want my previously defined macro programs to be automatically available when I submit a new job, so that I can use macros defined in earlier jobs without redefining them.

#### Acceptance Criteria

1. WHEN Job_Orchestrator prepares preamble code for a new job, THE Macro_Program_Store SHALL check if Macros_File exists in Session_Folder
2. IF Macros_File exists, THEN THE Macro_Program_Store SHALL read the complete file content
3. IF Macros_File does not exist, THEN THE Macro_Program_Store SHALL return empty string
4. THE Macro_Program_Store SHALL populate In_Memory_Cache with loaded macro program source code for subsequent fast access
5. IF file read fails, THEN THE Macro_Program_Store SHALL log a warning and return the current In_Memory_Cache state

### Requirement 4: Inject Macro Programs into Preamble Code

**User Story:** As a SAS user, I want my saved macro programs to be executed before my submitted code runs, so that the macros are available for use in my current job.

#### Acceptance Criteria

1. WHEN Preamble_Builder constructs preamble code, THE Preamble_Builder SHALL request macro program source from Macro_Program_Store
2. THE Preamble_Builder SHALL inject the macro program source code after library definitions and before macro variable restoration
3. THE Preamble_Builder SHALL separate macro program injection from other preamble sections with comment markers
4. THE Preamble_Builder SHALL ensure macro programs are injected only once per job submission
5. WHERE no macro programs exist for a session, THE Preamble_Builder SHALL omit the macro program section from preamble

### Requirement 5: Extract Macro Programs Using SAS Catalog Procedures

**User Story:** As a developer, I want the system to reliably extract macro program definitions from the SAS session, so that all user-defined macros are captured regardless of how they were defined.

#### Acceptance Criteria

1. THE Log_Parser SHALL inject catalog extraction code into the preamble or postamble that lists SASMACR_Catalog entries
2. THE Log_Parser SHALL use `PROC CATALOG` with `CATALOG=WORK.SASMACR` to enumerate all macro entries
3. THE Log_Parser SHALL use `%COPY` statement or catalog `SOURCE` entry to extract macro source code for each macro
4. THE Log_Parser SHALL write extracted macro source to the log using `%PUT` statements with distinctive markers for parsing
5. THE Log_Parser SHALL parse the log to identify macro extraction markers and reconstruct macro source code
6. THE Log_Parser SHALL handle macro source code containing special characters, quotes, and multi-line definitions
7. IF catalog access fails, THEN THE Log_Parser SHALL log an error and return an empty macro dictionary

### Requirement 6: Maintain In-Memory Cache for Performance

**User Story:** As a system administrator, I want macro program access to remain fast during job execution, so that the persistence layer doesn't slow down job submission.

#### Acceptance Criteria

1. WHEN macro programs are retrieved for a session, THE Macro_Program_Store SHALL return source code from In_Memory_Cache if present
2. WHEN macro programs are updated, THE Macro_Program_Store SHALL update In_Memory_Cache immediately
3. THE Macro_Program_Store SHALL use In_Memory_Cache as the source of truth during runtime
4. THE Macro_Program_Store SHALL only access Macros_File when In_Memory_Cache is empty for a given session
5. THE Macro_Program_Store SHALL maintain thread-safety using ConcurrentDictionary for In_Memory_Cache

### Requirement 7: Handle Concurrent Access Safely

**User Story:** As a system administrator, I want the persistence layer to handle concurrent job submissions safely, so that macro programs are not corrupted when multiple jobs run simultaneously for the same session.

#### Acceptance Criteria

1. WHEN multiple threads update macro programs for the same session concurrently, THE Macro_Program_Store SHALL ensure In_Memory_Cache updates are atomic
2. WHEN writing Macros_File, THE Macro_Program_Store SHALL use file system locking or atomic write operations to prevent corruption
3. IF a file write operation is in progress, THEN THE Macro_Program_Store SHALL queue subsequent writes or use the latest in-memory state
4. THE Macro_Program_Store SHALL preserve thread-safety guarantees consistent with existing MacroVarStore implementation
5. THE Macro_Program_Store SHALL not introduce race conditions between file I/O and in-memory operations

### Requirement 8: Follow MacroVarStore Architecture Pattern

**User Story:** As a developer, I want the macro program persistence implementation to follow the same patterns as macro variable persistence, so that the codebase remains consistent and maintainable.

#### Acceptance Criteria

1. THE Macro_Program_Store SHALL implement an IMacroProgramStore interface with GetAsync, SetAsync methods
2. THE Macro_Program_Store SHALL accept sessionId parameter for all storage operations
3. THE Macro_Program_Store SHALL be registered as a singleton service in dependency injection
4. THE Macro_Program_Store SHALL use IConfiguration to access SessionStorage:StudyFolder configuration
5. THE Macro_Program_Store SHALL implement lazy loading (load from disk only when In_Memory_Cache is empty)
6. THE Macro_Program_Store SHALL use the same session folder path resolution pattern as MacroVarStore
7. THE Macro_Program_Store SHALL use RegisterSession method pattern for sessionId-to-userId mapping

### Requirement 9: Handle File System Errors Gracefully

**User Story:** As a system administrator, I want the application to continue working even if file system errors occur, so that temporary I/O issues don't break active SAS sessions.

#### Acceptance Criteria

1. IF Macros_File cannot be written due to permissions, disk space, or I/O errors, THEN THE Macro_Program_Store SHALL log the error at warning level
2. IF Macros_File cannot be read due to file corruption or format errors, THEN THE Macro_Program_Store SHALL log the error and return an empty string
3. THE Macro_Program_Store SHALL continue serving macro programs from In_Memory_Cache when file operations fail
4. THE Macro_Program_Store SHALL not throw exceptions that bubble up to Job_Orchestrator during normal operation
5. WHEN file operations fail, THE Macro_Program_Store SHALL include session ID and file path in log messages for debugging

### Requirement 10: Validate Macro Source Code Syntax

**User Story:** As a developer, I want the system to validate macro source code before persisting it, so that corrupted or invalid macros don't cause job failures on subsequent submissions.

#### Acceptance Criteria

1. WHEN persisting macro programs, THE Macro_Program_Store SHALL verify each macro contains matching `%macro` and `%mend` statements
2. THE Macro_Program_Store SHALL verify macro names are valid SAS identifiers (alphanumeric and underscore, starting with letter or underscore)
3. THE Macro_Program_Store SHALL verify macro source does not contain unbalanced parentheses or quotes
4. IF a macro fails validation, THEN THE Macro_Program_Store SHALL log a warning with the macro name and validation error
5. THE Macro_Program_Store SHALL exclude invalid macros from Macros_File but continue persisting valid macros

### Requirement 11: Support Session Folder Path Resolution

**User Story:** As a system administrator, I want the persistence layer to correctly resolve session folder paths, so that macro programs are stored in the correct location per user and session.

#### Acceptance Criteria

1. THE Macro_Program_Store SHALL require IConfiguration dependency to access SessionStorage:StudyFolder configuration
2. THE Macro_Program_Store SHALL construct Session_Folder path as `{StudyFolder}/sessions/{userId}/{sessionId}/`
3. THE Macro_Program_Store SHALL store sessionId-to-userId mapping using RegisterSession method called by Job_Orchestrator
4. THE Macro_Program_Store SHALL handle path separators correctly on Windows and Unix systems
5. IF SessionStorage:StudyFolder configuration is missing, THEN THE Macro_Program_Store SHALL log an error and fall back to in-memory-only mode

### Requirement 12: Provide Observable Logging for Debugging

**User Story:** As a developer debugging session issues, I want to see when macro programs are loaded from or saved to disk, so that I can troubleshoot persistence problems.

#### Acceptance Criteria

1. WHEN Macros_File is successfully written, THE Macro_Program_Store SHALL log at information level with session ID and macro count
2. WHEN Macros_File is successfully loaded, THE Macro_Program_Store SHALL log at information level with session ID and macro count
3. WHEN file operations fail, THE Macro_Program_Store SHALL log at warning level with session ID, operation type, and error details
4. WHEN Macros_File does not exist on first read, THE Macro_Program_Store SHALL log at debug level indicating new session
5. THE Macro_Program_Store SHALL use structured logging with sessionId, userId, and filePath as properties
6. WHEN macro extraction from catalog fails, THE Log_Parser SHALL log at warning level with error details

### Requirement 13: Initialize Session Storage on First Access

**User Story:** As a SAS user starting a new session, I want the system to automatically create my session folder structure, so that I don't encounter errors when the first job saves macro programs.

#### Acceptance Criteria

1. WHEN writing Macros_File for a new session, THE Macro_Program_Store SHALL check if Session_Folder exists
2. IF Session_Folder does not exist, THEN THE Macro_Program_Store SHALL create the full directory path including parent directories
3. THE Macro_Program_Store SHALL handle directory creation failures gracefully and log warnings
4. THE Macro_Program_Store SHALL not fail the macro program update operation if directory creation succeeds but file write fails
5. WHERE Session_Folder already exists, THE Macro_Program_Store SHALL not attempt directory creation

### Requirement 14: Parse Macro Programs with Pretty Printer

**User Story:** As a developer, I want the system to include a reliable round-trip mechanism for macro program parsing and serialization, so that macro definitions are preserved accurately.

#### Acceptance Criteria

1. THE Macro_Program_Store SHALL format macro programs in Macros_File with consistent indentation and line breaks
2. THE Macro_Program_Store SHALL preserve macro parameter lists, local variable declarations, and macro body formatting
3. WHEN reading Macros_File, THE Macro_Program_Store SHALL parse the entire file as SAS source code without modification
4. THE Macro_Program_Store SHALL preserve comments within macro definitions when persisting to Macros_File
5. FOR ALL valid macro programs, writing then reading Macros_File SHALL produce equivalent executable SAS code (round-trip property)

### Requirement 15: Handle Macro Program Updates Across Jobs

**User Story:** As a SAS user, I want macro program redefinitions to overwrite previous versions, so that I can iteratively develop and refine my macros across multiple job submissions.

#### Acceptance Criteria

1. WHEN a macro program is redefined in a new job, THE Macro_Program_Store SHALL replace the previous definition in In_Memory_Cache
2. WHEN writing Macros_File, THE Macro_Program_Store SHALL include only the most recent definition of each macro
3. THE Macro_Program_Store SHALL not append duplicate macro definitions to Macros_File
4. WHEN a macro is redefined, THE Macro_Program_Store SHALL log at debug level indicating the macro was updated
5. THE Macro_Program_Store SHALL preserve all other macro definitions when updating a single macro
