# User Workflows

These workflows describe the intended MVP behavior from the user's point of view.

## Instructor: create and organize a course

1. Open the dashboard.
2. Navigate to Courses.
3. Create a course and enter its basic details.
4. Add modules or weekly units.
5. Add learning pages or materials to each unit.
6. Save changes and publish the course when it is ready.
7. Confirm that the course and its content appear as expected.

## Instructor: manage course enrollment

1. Open a course the instructor owns.
2. Explicitly enroll an active Learner account. This action immediately
   activates that learner's enrollment; creating or publishing the course does
   not enroll learners.
3. Suspend or reactivate an enrollment as needed, or remove it when access
   should end.
4. Explicitly reactivate a withdrawn or removed enrollment to re-enroll that
   learner.

Only the course-owning Instructor can perform these enrollment-management
actions. Learners cannot self-enroll or request enrollment, and enrollment does
not change an account's global role.

## Instructor: create an assignment and review work

1. Open a course.
2. Create an assignment with a title, instructions, and due date.
3. Make the assignment available to learners.
4. Open the submissions for the assignment.
5. Review a learner's work.
6. Provide feedback and, when applicable, a grade.
7. Save the review.

## Learner: access course material

1. Open the dashboard.
2. Select a published course in which the learner has an active enrollment.
3. Navigate to the relevant module or week.
4. Read the learning page or material.
5. Review announcements and activity instructions.
6. Participate in any included discussion activity.

Learners cannot access Draft courses, even if they have an active enrollment.
Changing a course to Draft blocks access until it is published again; it does
not change the enrollment.
Learners may withdraw their own active or suspended enrollment; the course
owner must explicitly re-enroll them before they regain course access.

## Learner: submit an assignment and view feedback

1. Open the course and select the assignment.
2. Read the instructions and due date.
3. Enter or attach the required work.
4. Submit the work and confirm the submission status.
5. Return to the assignment after review.
6. Read the instructor's feedback and view the grade if one was assigned.

## Workflow expectations

- Users should receive clear confirmation when a save or submission succeeds.
- Validation errors should explain what needs to be corrected.
- Learners must not be able to see another learner's submissions, grades, or private feedback.
- Instructors should be able to tell which submissions need review.
- An active enrollment exists independently of a course's Published or Draft
  status; only active enrollments in Published courses grant learner access.
