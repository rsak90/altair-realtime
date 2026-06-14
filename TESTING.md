Testing Guide: Multi-Submission with Session Persistence
How It Works
Your application maintains session state across multiple code submissions:

Session Library Path: LIBNAME SESSLIB "/sas/sessions/{userId}/{sessionId}/";

Each session gets a unique folder on the SLC Hub server
Datasets created in SESSLIB persist across submissions
Macro Variables:

Any macro variables defined using %let are captured via %put _user_;
They're parsed from logs and stored in MacroVarStore
Automatically included in the preamble of subsequent submissions
Test Scenario: Create and Read Dataset
Submission 1: Create a Dataset
/* Create a working dataset */
data SESSLIB.employees;
    input empid name $ salary;
    datalines;
101 John 50000
102 Mary 60000
103 Bob 55000
;
run;

/* Set some macro variables for next run */
%let REPORT_TITLE=Employee Analysis;
%let DEPT=Sales;
What happens:

Dataset employees is saved to /sas/sessions/{userId}/{sessionId}/employees.sas7bdat
Macro variables REPORT_TITLE and DEPT are captured and stored
Log shows: NOTE: The data set SESSLIB.EMPLOYEES has X observations...
Submission 2: Read and Process the Dataset
/* Macro variables from previous run are automatically available */
%put Report: &REPORT_TITLE;
%put Department: &DEPT;

/* Read the persisted dataset */
proc print data=SESSLIB.employees;
    title "&REPORT_TITLE - &DEPT Department";
run;

/* Create a filtered version */
data SESSLIB.high_earners;
    set SESSLIB.employees;
    where salary > 55000;
run;

/* Update macro variable */
%let LAST_RUN=%sysfunc(datetime(), datetime20.);
What happens:

Preamble automatically includes %let REPORT_TITLE=Employee Analysis;
Preamble automatically includes %let DEPT=Sales;
Reads existing SESSLIB.employees dataset
Creates new SESSLIB.high_earners dataset
Updates LAST_RUN macro variable for next submission
Submission 3: Further Analysis
/* All previous macro variables are available */
%put Last analysis was run at: &LAST_RUN;

/* Both datasets are available */
proc means data=SESSLIB.employees;
    var salary;
    title "Salary Statistics - &REPORT_TITLE";
run;

proc sql;
    select count(*) as high_earner_count
    from SESSLIB.high_earners;
quit;
Testing Steps
Start the Application

cd SasJobRunner
dotnet run
Open Browser → Navigate to the editor page

Ensure Same Session

Stay logged in with the same user
Keep the same browser session (don't clear cookies)
The sessionId is maintained in the session state
Submit Code Sequentially

Submit creation code first
Wait for "JobComplete" message
Submit reading code second
Verify dataset is accessible
How to Verify Session Persistence
Check Macro Variables:
/* In any submission, print all user macro variables */
%put _user_;
This will show all macro variables from previous runs.

Check Available Datasets:
/* List all datasets in session library */
proc datasets library=SESSLIB;
run;
quit;
Verify Session Path:
/* Print the actual library path being used */
proc sql;
    select libname, path 
    from dictionary.libnames 
    where libname='SESSLIB';
quit;
Important Notes
Session Scope:

Datasets persist as long as the session folder exists on SLC Hub
Macro variables are stored in memory (in MacroVarStore)
Clearing browser cookies/session = new sessionId = new empty folder
Dataset Naming:

Always use SESSLIB.datasetname (not just datasetname)
Dataset names must follow SAS naming rules (letters, numbers, underscore)
Macro Variables:

Automatically captured from %put _user_; (appended to every submission)
Only user-defined variables are captured (system vars like SESSIONID excluded)
Updated with each submission
Error Handling:

If a dataset doesn't exist, you'll see: ERROR: File SESSLIB.datasetname does not exist
Check the log to verify the dataset was actually created
Example Test Flow
Test 1: Simple Counter

/* First run - initialize */
%let COUNTER=1;
data SESSLIB.test;
    x = &COUNTER;
run;
/* Second run - increment */
%let COUNTER=%eval(&COUNTER + 1);
data SESSLIB.test;
    x = &COUNTER;
run;
/* Third run - verify */
%put Counter is now: &COUNTER;
proc print data=SESSLIB.test;
run;
This tests that macro variables carry forward properly!