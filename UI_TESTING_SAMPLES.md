# UI Testing Samples - Complete Test Data

## Overview

This document provides **copy-paste ready SAS code** for testing macro variables, macros, and options through the UI. Each test includes what to type in the editor, what to look for in the output, and how to verify persistence.

---

## Part 1: Testing Macro Variables (Currently Implemented)

### Test 1.1: Basic Macro Variables

**What to type in the editor:**
```sas
/* Define simple macro variables */
%let study_name = CardiacTrial2024;
%let investigator = Dr Smith;
%let site_count = 15;

/* Use them immediately */
%put NOTE: Study Name: &study_name;
%put NOTE: Investigator: &investigator;
%put NOTE: Number of Sites: &site_count;
```

**What to expect in the log:**
```
NOTE: Study Name: CardiacTrial2024
NOTE: Investigator: Dr Smith
NOTE: Number of Sites: 15
```

**Verification:**
- Check file: `variables.json` should contain:
  ```json
  {
    "variables": {
      "STUDY_NAME": "CardiacTrial2024",
      "INVESTIGATOR": "Dr Smith",
      "SITE_COUNT": "15"
    }
  }
  ```

**Next submission (in same session):**
```sas
/* Verify variables persisted */
%put NOTE: Previous study: &study_name;
%put NOTE: Previous investigator: &investigator;

/* Add new variable */
%let phase = Phase3;
%put NOTE: Phase: &phase;
```

**Expected:**
```
NOTE: Previous study: CardiacTrial2024  ← From previous submission!
NOTE: Previous investigator: Dr Smith  ← From previous submission!
NOTE: Phase: Phase3
```

---

### Test 1.2: Numeric Calculations with Macro Variables

**What to type:**
```sas
/* Define numeric parameters */
%let patient_target = 500;
%let enrolled = 423;
%let dropout_rate = 8.5;

/* Calculate remaining (SAS evaluates in macro context) */
%let remaining = %eval(&patient_target - &enrolled);

/* Display results */
%put NOTE: ===========================================;
%put NOTE: Clinical Trial Enrollment Status;
%put NOTE: ===========================================;
%put NOTE: Target Enrollment: &patient_target patients;
%put NOTE: Currently Enrolled: &enrolled patients;
%put NOTE: Remaining: &remaining patients;
%put NOTE: Dropout Rate: &dropout_rate percent;
%put NOTE: ===========================================;
```

**What to expect:**
```
NOTE: ===========================================
NOTE: Clinical Trial Enrollment Status
NOTE: ===========================================
NOTE: Target Enrollment: 500 patients
NOTE: Currently Enrolled: 423 patients
NOTE: Remaining: 77 patients
NOTE: Dropout Rate: 8.5 percent
NOTE: ===========================================
```

**Next submission:**
```sas
/* Update enrollment */
%let enrolled = 431;
%let remaining = %eval(&patient_target - &enrolled);

%put NOTE: Updated Enrollment: &enrolled;
%put NOTE: Updated Remaining: &remaining;
%put NOTE: Target still: &patient_target;
```

---

### Test 1.3: Conditional Logic with Macro Variables

**What to type:**
```sas
/* Define trial parameters */
%let trial_status = active;
%let enrollment_complete = 0;
%let data_locked = 0;

/* Check trial status */
%put NOTE: Trial Status: &trial_status;

%if &trial_status = active %then %do;
    %put NOTE: Trial is ACTIVE - continuing enrollment;
    %let next_action = Continue recruitment;
%end;
%else %if &trial_status = complete %then %do;
    %put NOTE: Trial is COMPLETE - begin analysis;
    %let next_action = Start data analysis;
%end;

%put NOTE: Next Action: &next_action;
```

**What to expect:**
```
NOTE: Trial Status: active
NOTE: Trial is ACTIVE - continuing enrollment
NOTE: Next Action: Continue recruitment
```

**Next submission (change status):**
```sas
/* Update trial status */
%let trial_status = complete;
%let enrollment_complete = 1;

%if &trial_status = complete %then %do;
    %put NOTE: Trial completed - beginning closeout;
    %let next_action = Database lock and analysis;
%end;

%put NOTE: Updated Status: &trial_status;
%put NOTE: Next Action: &next_action;
```

---

### Test 1.4: Path and File Macro Variables

**What to type:**
```sas
/* Define file paths */
%let data_dir = /clinical/data/cardiac_trial;
%let output_dir = /clinical/output/reports;
%let log_dir = /clinical/logs;

/* Define file names */
%let patient_file = patients_baseline.csv;
%let results_file = trial_results_&sysdate..xlsx;

/* Build full paths */
%let patient_path = &data_dir/&patient_file;
%let results_path = &output_dir/&results_file;

/* Display configuration */
%put NOTE: ========================================;
%put NOTE: File System Configuration;
%put NOTE: ========================================;
%put NOTE: Data Directory: &data_dir;
%put NOTE: Output Directory: &output_dir;
%put NOTE: Log Directory: &log_dir;
%put NOTE: Patient File Path: &patient_path;
%put NOTE: Results File Path: &results_path;
%put NOTE: ========================================;
```

**What to expect:**
```
NOTE: ========================================
NOTE: File System Configuration
NOTE: ========================================
NOTE: Data Directory: /clinical/data/cardiac_trial
NOTE: Output Directory: /clinical/output/reports
NOTE: Log Directory: /clinical/logs
NOTE: Patient File Path: /clinical/data/cardiac_trial/patients_baseline.csv
NOTE: Results File Path: /clinical/output/reports/trial_results_...xlsx
NOTE: ========================================
```

---

### Test 1.5: Complex String Variables

**What to type:**
```sas
/* Define complex strings */
%let study_title = A Randomized Double-Blind Placebo-Controlled Trial;
%let inclusion_criteria = Age 18-65 AND diagnosed hypertension AND no prior MI;
%let protocol_version = v2.3.1-2024-06-18;
%let contact_email = clinical.trials@hospital.org;

/* SQL-like strings */
%let sql_where = WHERE age BETWEEN 18 AND 65 AND diagnosis='HTN';

/* Display */
%put NOTE: Study Title: &study_title;
%put NOTE: Inclusion: &inclusion_criteria;
%put NOTE: Protocol: &protocol_version;
%put NOTE: Contact: &contact_email;
%put NOTE: SQL Filter: &sql_where;
```

**What to expect:**
All strings preserved exactly, including spaces and special characters.

---

### Test 1.6: Using Variables in Dataset Creation

**What to type:**
```sas
/* Define parameters */
%let dataset_name = patient_cohort;
%let total_patients = 5;
%let study_arm = Treatment_A;

/* Create dataset using macro variables */
data SESSLIB.&dataset_name;
    length study_arm $20;
    study_arm = "&study_arm";
    
    do patient_id = 1 to &total_patients;
        age = 45 + int(ranuni(123) * 20);
        output;
    end;
run;

/* Verify creation */
proc print data=SESSLIB.&dataset_name;
    title "Dataset: &dataset_name (Study Arm: &study_arm)";
run;
```

**What to expect:**
- Dataset created with name from variable
- 5 patients generated
- Study arm field populated

**Next submission:**
```sas
/* Variables persist - use them again */
%put NOTE: Previous dataset: &dataset_name;
%put NOTE: Previous study arm: &study_arm;
%put NOTE: Previous count: &total_patients;

/* Create another dataset */
%let dataset_name = patient_followup;
%let study_arm = Treatment_B;

data SESSLIB.&dataset_name;
    input patient_id visit_date :date9. status $;
    format visit_date date9.;
    datalines;
101 15JUN2024 Active
102 15JUN2024 Completed
103 16JUN2024 Active
;
run;

proc print data=SESSLIB.&dataset_name;
    title "&dataset_name Dataset";
run;
```

---

## Part 2: Testing Macros (If Implemented)

> **Note:** Currently, only macro VARIABLES are persisted. If you implement macro DEFINITION persistence, use these tests.

### Test 2.1: Simple Macro Definition

**What to type:**
```sas
/* Define a simple macro */
%macro greet(name);
    %put NOTE: Hello, &name!;
    %put NOTE: Welcome to the Clinical Trial System;
%mend greet;

/* Use it */
%greet(Dr Smith);
%greet(Study Coordinator);
```

**What to expect:**
```
NOTE: Hello, Dr Smith!
NOTE: Welcome to the Clinical Trial System
NOTE: Hello, Study Coordinator!
NOTE: Welcome to the Clinical Trial System
```

**If macros are persisted:**
```json
{
  "macros": {
    "GREET": {
      "definition": "%macro greet(name); ...",
      "parameters": ["name"]
    }
  }
}
```

**Next submission (test persistence):**
```sas
/* Try to use macro from previous session */
%greet(New User);
```

**Expected if persisted:**
```
NOTE: Hello, New User!
NOTE: Welcome to the Clinical Trial System
```

---

### Test 2.2: Macro with Calculations

**What to type:**
```sas
/* Define calculation macro */
%macro calc_bmi(weight, height);
    %let bmi = %sysevalf(&weight / (&height * &height));
    %put NOTE: Weight: &weight kg, Height: &height m;
    %put NOTE: Calculated BMI: &bmi;
%mend calc_bmi;

/* Use it */
%calc_bmi(75, 1.75);
%calc_bmi(90, 1.80);
%calc_bmi(68, 1.65);
```

**What to expect:**
```
NOTE: Weight: 75 kg, Height: 1.75 m
NOTE: Calculated BMI: 24.48979...
NOTE: Weight: 90 kg, Height: 1.80 m
NOTE: Calculated BMI: 27.77777...
NOTE: Weight: 68 kg, Height: 1.65 m
NOTE: Calculated BMI: 24.97779...
```

---

### Test 2.3: Macro Generating Dataset

**What to type:**
```sas
/* Define macro to create test patients */
%macro create_patients(count, arm);
    data SESSLIB.patients_&arm;
        length study_arm $20;
        study_arm = "&arm";
        
        do patient_id = 1 to &count;
            age = 40 + int(ranuni(123) * 30);
            weight = 60 + int(ranuni(456) * 40);
            output;
        end;
    run;
    
    %put NOTE: Created &count patients for arm: &arm;
%mend create_patients;

/* Use it */
%create_patients(10, TreatmentA);
%create_patients(12, TreatmentB);
%create_patients(8, Placebo);

/* Verify */
proc print data=SESSLIB.patients_TreatmentA (obs=5);
    title "Treatment A - First 5 Patients";
run;
```

---

## Part 3: Testing SAS Options (If Implemented)

> **Note:** If you implement SAS options persistence, use these tests.

### Test 3.1: Common SAS Options

**What to type:**
```sas
/* Set common options */
options pagesize=60;
options linesize=120;
options nodate nonumber;
options fmtsearch=(SESSLIB WORK);

/* Verify settings */
proc options option=pagesize;
run;

proc options option=linesize;
run;

%put NOTE: Page Size: %sysfunc(getoption(pagesize));
%put NOTE: Line Size: %sysfunc(getoption(linesize));
```

**What to expect:**
```
PAGESIZE=60
LINESIZE=120
NOTE: Page Size: 60
NOTE: Line Size: 120
```

**If options are persisted:**
```json
{
  "options": {
    "pagesize": "60",
    "linesize": "120",
    "date": "nodate",
    "number": "nonumber"
  }
}
```

---

### Test 3.2: Format Search Path

**What to type:**
```sas
/* Set format library path */
options fmtsearch=(SESSLIB.FORMATS WORK LIBRARY);

/* Display current setting */
%put NOTE: Format Search: %sysfunc(getoption(fmtsearch));

/* Create a format */
proc format library=SESSLIB.FORMATS;
    value status_fmt
        1 = 'Active'
        2 = 'Completed'
        3 = 'Withdrawn';
run;

/* Use it */
data test;
    status_code = 1; output;
    status_code = 2; output;
    status_code = 3; output;
run;

proc print data=test;
    format status_code status_fmt.;
run;
```

---

## Part 4: Integration Tests (Variables + Datasets)

### Test 4.1: Complete Clinical Trial Workflow

**Submission 1: Setup Trial Parameters**
```sas
/* Define trial configuration */
%let trial_id = CT2024-001;
%let trial_name = Cardiac Safety Study;
%let start_date = 01JAN2024;
%let target_enrollment = 300;
%let current_enrollment = 0;

/* Define site information */
%let site_id = SITE-001;
%let site_name = Memorial Hospital;
%let principal_investigator = Dr Johnson;

/* Display configuration */
%put NOTE: ==========================================;
%put NOTE: Trial Configuration;
%put NOTE: ==========================================;
%put NOTE: Trial ID: &trial_id;
%put NOTE: Trial Name: &trial_name;
%put NOTE: Start Date: &start_date;
%put NOTE: Target Enrollment: &target_enrollment;
%put NOTE: Site: &site_name (&site_id);
%put NOTE: PI: &principal_investigator;
%put NOTE: ==========================================;
```

**Submission 2: Enroll First Patients**
```sas
/* Use persisted variables from previous submission */
%put NOTE: Enrolling patients for trial: &trial_name (&trial_id);
%put NOTE: Site: &site_name;

/* Create patient dataset */
data SESSLIB.patients_&site_id;
    length trial_id $20 site_id $20 pi $50;
    trial_id = "&trial_id";
    site_id = "&site_id";
    pi = "&principal_investigator";
    
    /* Generate 5 patients */
    array baseline_dates[5] _temporary_ 
        ('15JUN2024' '15JUN2024' '16JUN2024' '17JUN2024' '18JUN2024');
    
    do i = 1 to 5;
        patient_id = cats(&site_id, '-', put(i, z3.));
        enrollment_date = input(baseline_dates[i], date9.);
        age = 45 + int(ranuni(123) * 25);
        gender = choose(mod(i,2)+1, 'M', 'F');
        output;
    end;
    
    drop i;
    format enrollment_date date9.;
run;

/* Update enrollment count */
%let current_enrollment = 5;

/* Report */
proc print data=SESSLIB.patients_&site_id;
    title "Enrolled Patients - &site_name";
    title2 "Trial: &trial_name (&trial_id)";
run;

%put NOTE: Updated enrollment: &current_enrollment / &target_enrollment;
```

**Submission 3: Record Baseline Measurements**
```sas
/* Use persisted trial info */
%put NOTE: Recording baseline for trial: &trial_id;
%put NOTE: Current enrollment: &current_enrollment patients;

/* Create baseline measurements dataset */
data SESSLIB.baseline_&site_id;
    set SESSLIB.patients_&site_id;
    
    /* Generate baseline vitals */
    systolic_bp = 120 + int(ranuni(456) * 40);
    diastolic_bp = 70 + int(ranuni(789) * 20);
    heart_rate = 60 + int(ranuni(321) * 30);
    weight_kg = 60 + int(ranuni(654) * 40);
    height_cm = 160 + int(ranuni(987) * 30);
    
    /* Calculate BMI */
    bmi = weight_kg / ((height_cm/100) ** 2);
run;

/* Summary report */
proc means data=SESSLIB.baseline_&site_id n mean std min max;
    var systolic_bp diastolic_bp heart_rate weight_kg height_cm bmi;
    title "Baseline Measurements Summary";
    title2 "Trial: &trial_name - Site: &site_name";
run;
```

**Submission 4: Enroll More Patients**
```sas
/* Add 3 more patients */
data SESSLIB.patients_&site_id;
    set SESSLIB.patients_&site_id;
    output;
    
    /* Only add new patients if not already done */
    if _n_ = 1 then do;
        do i = 6 to 8;
            patient_id = cats("&site_id", '-', put(i, z3.));
            trial_id = "&trial_id";
            site_id = "&site_id";
            pi = "&principal_investigator";
            enrollment_date = today();
            age = 45 + int(ranuni(123) * 25);
            gender = choose(mod(i,2)+1, 'M', 'F');
            output;
        end;
    end;
run;

/* Update count */
%let current_enrollment = 8;

%put NOTE: Enrollment Progress: &current_enrollment / &target_enrollment;
%put NOTE: Completion: %sysevalf(&current_enrollment / &target_enrollment * 100) percent;
```

---

### Test 4.2: Multi-Visit Tracking

**Submission 1: Setup Visit Schedule**
```sas
/* Define visit parameters */
%let study_id = LONG-TERM-001;
%let visit_count = 4;
%let visit_interval = 30;

/* Define visits */
%let visit1_name = Baseline;
%let visit2_name = Month_1;
%let visit3_name = Month_2;
%let visit4_name = Month_3;

%put NOTE: Study: &study_id;
%put NOTE: Total Visits: &visit_count;
%put NOTE: Interval: &visit_interval days;
```

**Submission 2: Create Visit Schedule**
```sas
/* Use persisted parameters */
data SESSLIB.visit_schedule;
    length study_id $20 visit_name $20;
    study_id = "&study_id";
    
    /* Visit 1 */
    visit_num = 1;
    visit_name = "&visit1_name";
    day_from_baseline = 0;
    output;
    
    /* Visit 2 */
    visit_num = 2;
    visit_name = "&visit2_name";
    day_from_baseline = &visit_interval;
    output;
    
    /* Visit 3 */
    visit_num = 3;
    visit_name = "&visit3_name";
    day_from_baseline = &visit_interval * 2;
    output;
    
    /* Visit 4 */
    visit_num = 4;
    visit_name = "&visit4_name";
    day_from_baseline = &visit_interval * 3;
    output;
run;

proc print data=SESSLIB.visit_schedule;
    title "Visit Schedule for Study: &study_id";
run;
```

**Submission 3: Record Visit Data**
```sas
/* Record data for a visit */
%let current_visit = 2;
%let current_visit_name = &visit2_name;

data SESSLIB.visit_data_&current_visit;
    length study_id $20 visit_name $20;
    study_id = "&study_id";
    visit_num = &current_visit;
    visit_name = "&current_visit_name";
    visit_date = today();
    
    do patient_num = 1 to 5;
        patient_id = put(patient_num, z3.);
        temperature = 36.5 + ranuni(123) * 1.5;
        blood_pressure = 120 + int(ranuni(456) * 30);
        notes = "Visit &current_visit completed";
        output;
    end;
    
    format visit_date date9.;
run;

%put NOTE: Recorded data for &current_visit_name (Visit &current_visit);
%put NOTE: Study: &study_id;
```

---

## Part 5: Error Scenarios and Edge Cases

### Test 5.1: Empty Variables

**What to type:**
```sas
/* Test empty values */
%let empty_var = ;
%let space_var =  ;
%let null_text = (null);

%put NOTE: Empty var: [&empty_var];
%put NOTE: Space var: [&space_var];
%put NOTE: Null text: [&null_text];
```

---

### Test 5.2: Special Characters

**What to type:**
```sas
/* Test special characters */
%let path_unix = /data/clinical/trial;
%let path_windows = C:\Data\Clinical\Trial;
%let email = admin@trial.hospital.org;
%let symbols = !@#$%^&*()_+-={}[]|:;"'<>,.?/;
%let mixed = Test-Value_123.456;

%put NOTE: Unix Path: &path_unix;
%put NOTE: Windows Path: &path_windows;
%put NOTE: Email: &email;
%put NOTE: Symbols: &symbols;
%put NOTE: Mixed: &mixed;
```

---

### Test 5.3: Very Long Values

**What to type:**
```sas
/* Test long strings */
%let long_description = This is a very long description of a clinical trial that includes multiple sentences and should test the system's ability to handle lengthy text strings without truncation or corruption of the data when storing and retrieving from the persistence layer;

%put NOTE: Long Description:;
%put NOTE: &long_description;

/* Verify length */
%let desc_length = %length(&long_description);
%put NOTE: Length: &desc_length characters;
```

---

## Quick Reference: Testing Checklist

### For Each Test:

1. **Before Submission:**
   - [ ] Note current variables.json content
   - [ ] Clear browser console (F12)

2. **After Submission:**
   - [ ] Check job log for expected output
   - [ ] Verify variables.json file updated
   - [ ] Check application logs for "Successfully wrote"

3. **Next Submission:**
   - [ ] Verify variables available in preamble
   - [ ] Test modifications work
   - [ ] Verify file updates correctly

4. **After Restart:**
   - [ ] Stop application (Ctrl+C)
   - [ ] Start application (dotnet run)
   - [ ] Submit job using variables
   - [ ] Verify variables loaded from file

---

## Expected Performance

| Operation | Expected Time |
|-----------|---------------|
| Parse 10 variables | < 50ms |
| Write variables.json | < 200ms |
| Read variables.json | < 100ms |
| Build preamble with 20 vars | < 10ms |

---

## Troubleshooting Tips

### Variables Not Available in Next Submission

**Check:**
```sas
%put _user_;
```
This shows ALL current user variables.

### Verify File Content

**Windows:**
```bash
type "{StudyFolder}\sessions\{userId}\{sessionId}\variables.json"
```

**Unix:**
```bash
cat {StudyFolder}/sessions/{userId}/{sessionId}/variables.json
```

### Debug Variable Resolution

```sas
/* Check if variable is defined */
%put NOTE: PROJECT is: &project;
%put NOTE: All user vars:;
%put _user_;
```

---

## Copy-Paste Quick Tests

### 30-Second Test
```sas
%let test=hello;
%put NOTE: &test;
```
Submit twice - second time should show "hello".

### 2-Minute Test
```sas
%let a=1; %let b=2; %let c=3;
%put NOTE: &a &b &c;
```
Restart app, submit: `%put NOTE: &a &b &c;`
Should show: `1 2 3`

### 5-Minute Test
Use Test 4.1 above for complete workflow.

---

Happy Testing! 🎯
