
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS teammy;

-- ========== 1) Roles, Majors, Users ==========
CREATE TABLE IF NOT EXISTS teammy.roles (
  role_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name      TEXT NOT NULL UNIQUE CHECK (name IN ('admin','moderator','mentor','student'))
);

CREATE TABLE IF NOT EXISTS teammy.majors (
  major_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  major_name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS teammy.users (
  user_id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email            CITEXT NOT NULL UNIQUE,
  email_verified   BOOLEAN NOT NULL DEFAULT FALSE,
  display_name     TEXT NOT NULL,
  avatar_url       TEXT,
  phone            TEXT,
  student_code     VARCHAR(30),
  gender           TEXT,
  major_id         UUID REFERENCES teammy.majors(major_id) ON DELETE SET NULL,
  portfolio_url    TEXT,
  skills           JSONB,
  skills_completed BOOLEAN NOT NULL DEFAULT FALSE,
  is_active        BOOLEAN NOT NULL DEFAULT TRUE,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE teammy.users
  ADD COLUMN IF NOT EXISTS portfolio_url TEXT;

CREATE TABLE IF NOT EXISTS teammy.user_roles (
  user_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id      UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  role_id      UUID NOT NULL REFERENCES teammy.roles(role_id) ON DELETE CASCADE,
  UNIQUE (user_id, role_id)
);

-- ========== 2) Semesters & Policy ==========
CREATE TABLE IF NOT EXISTS teammy.semesters (
  semester_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  season      TEXT,
  year        INT,
  start_date  DATE,
  end_date    DATE,
  is_active   BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS teammy.semester_policy (
  semester_id  UUID PRIMARY KEY REFERENCES teammy.semesters(semester_id) ON DELETE CASCADE,
  team_self_select_start  DATE NOT NULL,
  team_self_select_end    DATE NOT NULL,
  team_suggest_start      DATE NOT NULL,
  topic_self_select_start DATE NOT NULL,
  topic_self_select_end   DATE NOT NULL,
  topic_suggest_start     DATE NOT NULL,
  desired_group_size_min  INT  NOT NULL DEFAULT 4 CHECK (desired_group_size_min > 0),
  desired_group_size_max  INT  NOT NULL DEFAULT 6 CHECK (desired_group_size_max >= 2)
);

-- ========== 3) Topics & Topic-Mentor ==========
CREATE TABLE IF NOT EXISTS teammy.topics (
  topic_id     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  semester_id  UUID NOT NULL REFERENCES teammy.semesters(semester_id) ON DELETE CASCADE,
  major_id     UUID REFERENCES teammy.majors(major_id) ON DELETE SET NULL,
  title        TEXT NOT NULL,
  description  TEXT,
  source       TEXT,
  source_file_name TEXT,
  source_file_type TEXT,
  source_file_size BIGINT,
  skills       JSONB,
  status       TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','closed','archived')),
  created_by   UUID NOT NULL REFERENCES teammy.users(user_id),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (semester_id, title)
);

ALTER TABLE teammy.topics
  ADD COLUMN IF NOT EXISTS source TEXT,
    ADD COLUMN IF NOT EXISTS source_file_name TEXT,
    ADD COLUMN IF NOT EXISTS source_file_type TEXT,
    ADD COLUMN IF NOT EXISTS source_file_size BIGINT,
    ADD COLUMN IF NOT EXISTS skills JSONB;

CREATE TABLE IF NOT EXISTS teammy.topics_mentor (
  topic_id  UUID NOT NULL REFERENCES teammy.topics(topic_id) ON DELETE CASCADE,
  mentor_id UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  PRIMARY KEY (topic_id, mentor_id)
);

-- ========== 4) Groups & Members ==========
CREATE TABLE IF NOT EXISTS teammy.groups (
  group_id     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  semester_id  UUID NOT NULL REFERENCES teammy.semesters(semester_id) ON DELETE CASCADE,
  topic_id     UUID REFERENCES teammy.topics(topic_id) ON DELETE SET NULL,
  mentor_id    UUID REFERENCES teammy.users(user_id) ON DELETE SET NULL,
  major_id     UUID REFERENCES teammy.majors(major_id) ON DELETE SET NULL,
  name         TEXT NOT NULL,
  description  TEXT,
  max_members  INT  NOT NULL CHECK (max_members > 0),
  status       TEXT NOT NULL DEFAULT 'recruiting' CHECK (status IN ('recruiting','active','closed')),
  skills       JSONB,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (semester_id, name)
);
CREATE INDEX IF NOT EXISTS ix_groups_skills_gin
  ON teammy.groups USING gin (skills jsonb_path_ops);

CREATE TABLE IF NOT EXISTS teammy.group_members (
  group_member_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id        UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  user_id         UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  semester_id     UUID NOT NULL REFERENCES teammy.semesters(semester_id),
  status          TEXT NOT NULL CHECK (status IN ('pending','member','leader','left','removed','failed','completed')),
  joined_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  left_at         TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS teammy.group_member_roles (
  group_member_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_member_id      UUID NOT NULL REFERENCES teammy.group_members(group_member_id) ON DELETE CASCADE,
  role_name            TEXT NOT NULL,
  assigned_by          UUID REFERENCES teammy.users(user_id),
  assigned_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (group_member_id, role_name)
);
CREATE INDEX IF NOT EXISTS ix_member_roles_member
  ON teammy.group_member_roles(group_member_id);

-- unique: 1 người/1 team/1 kỳ (active)
CREATE UNIQUE INDEX IF NOT EXISTS ux_member_user_semester_active
  ON teammy.group_members(user_id, semester_id)
  WHERE status IN ('pending','member','leader');

-- unique: 1 leader duy nhất / group
CREATE UNIQUE INDEX IF NOT EXISTS ux_group_single_leader
  ON teammy.group_members(group_id)
  WHERE status='leader';

-- trigger: semester member = semester group
CREATE OR REPLACE FUNCTION teammy.fn_member_semester_matches_group()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_group_sem UUID;
BEGIN
  SELECT semester_id INTO v_group_sem FROM teammy.groups WHERE group_id = NEW.group_id;
  IF NEW.semester_id IS DISTINCT FROM v_group_sem THEN
    RAISE EXCEPTION 'group_member.semester_id must equal groups.semester_id';
  END IF;
  RETURN NEW;
END$$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_member_semester_matches_group') THEN
    CREATE TRIGGER trg_member_semester_matches_group
    BEFORE INSERT OR UPDATE OF group_id, semester_id
    ON teammy.group_members
    FOR EACH ROW EXECUTE FUNCTION teammy.fn_member_semester_matches_group();
  END IF;
END$$;

-- trigger: enforce same major in group
CREATE OR REPLACE FUNCTION teammy.fn_enforce_same_major()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_group_major UUID; v_user_major UUID;
BEGIN
  SELECT major_id INTO v_group_major FROM teammy.groups WHERE group_id = NEW.group_id;
  SELECT major_id INTO v_user_major  FROM teammy.users  WHERE user_id  = NEW.user_id;

  IF v_group_major IS NULL THEN
    UPDATE teammy.groups SET major_id = v_user_major WHERE group_id = NEW.group_id;
  ELSIF v_group_major <> v_user_major THEN
    RAISE EXCEPTION 'User major must match group major';
  END IF;
  RETURN NEW;
END$$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_same_major_only') THEN
    CREATE TRIGGER trg_same_major_only
    BEFORE INSERT ON teammy.group_members
    FOR EACH ROW EXECUTE FUNCTION teammy.fn_enforce_same_major();
  END IF;
END$$;

-- unique: 1 topic chỉ gán cho 1 team
CREATE UNIQUE INDEX IF NOT EXISTS ux_group_unique_topic
  ON teammy.groups(topic_id)
  WHERE topic_id IS NOT NULL;

-- trigger: topic & group cùng semester + topic phải open
CREATE OR REPLACE FUNCTION teammy.fn_enforce_group_topic_rules()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_topic_sem UUID; v_topic_status TEXT;
BEGIN
  IF NEW.topic_id IS NULL THEN RETURN NEW; END IF;

  SELECT semester_id, status INTO v_topic_sem, v_topic_status
  FROM teammy.topics WHERE topic_id = NEW.topic_id;

  IF v_topic_sem IS NULL THEN
    RAISE EXCEPTION 'Topic not found';
  END IF;
  IF v_topic_sem <> NEW.semester_id THEN
    RAISE EXCEPTION 'Group and Topic must belong to the same semester';
  END IF;
  IF v_topic_status <> 'open' THEN
    RAISE EXCEPTION 'Topic must be open';
  END IF;
  RETURN NEW;
END$$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_enforce_group_topic_rules') THEN
    CREATE TRIGGER trg_enforce_group_topic_rules
    BEFORE INSERT OR UPDATE OF topic_id, semester_id
    ON teammy.groups
    FOR EACH ROW EXECUTE FUNCTION teammy.fn_enforce_group_topic_rules();
  END IF;
END$$;

-- ========== 5) Recruitment: posts / candidates / invitations ==========
CREATE TABLE IF NOT EXISTS teammy.recruitment_posts (
  post_id     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  semester_id UUID NOT NULL REFERENCES teammy.semesters(semester_id),
  post_type   TEXT NOT NULL CHECK (post_type IN ('group_hiring','individual')),
  group_id    UUID REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  user_id     UUID REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  major_id    UUID REFERENCES teammy.majors(major_id) ON DELETE SET NULL,
  title       TEXT NOT NULL,
  description TEXT,
  position_needed TEXT,
  required_skills JSONB,
  current_members INT,
  status      TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','closed','expired')),
  application_deadline TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT ck_rp_kind CHECK (
    (post_type='group_hiring' AND group_id IS NOT NULL AND user_id IS NULL)
    OR
    (post_type='individual'   AND user_id  IS NOT NULL AND group_id IS NULL)
  )
);
CREATE INDEX IF NOT EXISTS ix_posts_semester_status_type
  ON teammy.recruitment_posts(semester_id, status, post_type);

CREATE INDEX IF NOT EXISTS ix_rp_required_skills_gin
  ON teammy.recruitment_posts USING gin (required_skills jsonb_path_ops);

CREATE TABLE IF NOT EXISTS teammy.candidates (
  candidate_id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  post_id             UUID NOT NULL REFERENCES teammy.recruitment_posts(post_id) ON DELETE CASCADE,
  applicant_user_id   UUID REFERENCES teammy.users(user_id)  ON DELETE CASCADE,
  applicant_group_id  UUID REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  applied_by_user_id  UUID REFERENCES teammy.users(user_id) ON DELETE SET NULL,
  message             TEXT,
  status              TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','accepted','rejected','withdrawn','expired')),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cand_post_user
  ON teammy.candidates(post_id, applicant_user_id)
  WHERE applicant_user_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_cand_post_group
  ON teammy.candidates(post_id, applicant_group_id)
  WHERE applicant_group_id IS NOT NULL;

CREATE OR REPLACE FUNCTION teammy.fn_candidates_match_post_type()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_type TEXT;
BEGIN
  SELECT post_type INTO v_type FROM teammy.recruitment_posts WHERE post_id = NEW.post_id;
  IF v_type = 'group_hiring' THEN
    IF NEW.applicant_user_id IS NULL OR NEW.applicant_group_id IS NOT NULL THEN
      RAISE EXCEPTION 'For group_hiring: applicant_user_id required, applicant_group_id must be NULL';
    END IF;
  ELSIF v_type = 'individual' THEN
    IF NEW.applicant_group_id IS NULL OR NEW.applicant_user_id IS NOT NULL THEN
      RAISE EXCEPTION 'For individual: applicant_group_id required, applicant_user_id must be NULL';
    END IF;
  END IF;
  RETURN NEW;
END$$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_candidates_match_post_type') THEN
    CREATE TRIGGER trg_candidates_match_post_type
    BEFORE INSERT OR UPDATE OF post_id, applicant_user_id, applicant_group_id
    ON teammy.candidates FOR EACH ROW
    EXECUTE FUNCTION teammy.fn_candidates_match_post_type();
  END IF;
END$$;

CREATE TABLE IF NOT EXISTS teammy.invitations (
  invitation_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id        UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  invitee_user_id UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  invited_by      UUID NOT NULL REFERENCES teammy.users(user_id),
  topic_id        UUID REFERENCES teammy.topics(topic_id) ON DELETE CASCADE,
  status          TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','accepted','rejected','expired','revoked')),
  message         TEXT,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  responded_at    TIMESTAMPTZ,
  expires_at      TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_invite_pending_per_user
  ON teammy.invitations(group_id, invitee_user_id)
  WHERE status = 'pending';

-- ========== 6) Boards / Tasks / Comments / Files ==========
CREATE TABLE IF NOT EXISTS teammy.boards (
  board_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id   UUID NOT NULL UNIQUE REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  board_name TEXT NOT NULL DEFAULT 'Board',
  status     TEXT
);

CREATE TABLE IF NOT EXISTS teammy.columns (
  column_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  board_id    UUID NOT NULL REFERENCES teammy.boards(board_id) ON DELETE CASCADE,
  column_name TEXT NOT NULL,
  position    INT  NOT NULL,
  is_done     BOOLEAN NOT NULL DEFAULT FALSE,
  due_date    TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (board_id, column_name),
  UNIQUE (board_id, position)
);

CREATE TABLE IF NOT EXISTS teammy.tasks (
  task_id     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id    UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  column_id   UUID NOT NULL REFERENCES teammy.columns(column_id) ON DELETE CASCADE,
  title       TEXT NOT NULL,
  description TEXT,
  priority    TEXT,
  status      TEXT,
  due_date    TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS teammy.task_assignments (
  task_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id            UUID NOT NULL REFERENCES teammy.tasks(task_id) ON DELETE CASCADE,
  user_id            UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  assigned_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (task_id, user_id)
);

CREATE TABLE IF NOT EXISTS teammy.comments (
  comment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  task_id    UUID NOT NULL REFERENCES teammy.tasks(task_id) ON DELETE CASCADE,
  user_id    UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  content    TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS teammy.shared_files (
  file_id     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id    UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  uploaded_by UUID NOT NULL REFERENCES teammy.users(user_id),
  task_id     UUID REFERENCES teammy.tasks(task_id) ON DELETE SET NULL,
  file_name   TEXT,
  file_url    TEXT NOT NULL,
  file_type   TEXT,
  file_size   BIGINT,
  description TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE teammy.shared_files
  ADD COLUMN IF NOT EXISTS file_name TEXT;

-- ========== 6b) Backlog & Milestones ==========
CREATE TABLE IF NOT EXISTS teammy.backlog_items (
  backlog_item_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id        UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  title           TEXT NOT NULL,
  description     TEXT,
  priority        TEXT,
  status          TEXT NOT NULL DEFAULT 'planned'
                   CHECK (status IN ('planned','ready','in_progress','blocked','completed','archived')),
  category        TEXT,
  story_points    INT,
  owner_user_id   UUID REFERENCES teammy.users(user_id) ON DELETE SET NULL,
  created_by      UUID NOT NULL REFERENCES teammy.users(user_id),
  due_date        TIMESTAMPTZ,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_backlog_items_group_status
  ON teammy.backlog_items(group_id, status);
CREATE INDEX IF NOT EXISTS ix_backlog_items_owner
  ON teammy.backlog_items(owner_user_id);

ALTER TABLE teammy.tasks
  ADD COLUMN IF NOT EXISTS backlog_item_id UUID REFERENCES teammy.backlog_items(backlog_item_id) ON DELETE SET NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tasks_backlog_item
  ON teammy.tasks(backlog_item_id)
  WHERE backlog_item_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS teammy.milestones (
  milestone_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id     UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  name         TEXT NOT NULL,
  description  TEXT,
  target_date  DATE,
  status       TEXT NOT NULL DEFAULT 'planned'
                 CHECK (status IN ('planned','in_progress','completed','blocked','archived','slipped')),
  completed_at TIMESTAMPTZ,
  created_by   UUID NOT NULL REFERENCES teammy.users(user_id),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_milestones_group_status
  ON teammy.milestones(group_id, status);
CREATE INDEX IF NOT EXISTS ix_milestones_target_date
  ON teammy.milestones(target_date);

CREATE TABLE IF NOT EXISTS teammy.milestone_items (
  milestone_item_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  milestone_id      UUID NOT NULL REFERENCES teammy.milestones(milestone_id) ON DELETE CASCADE,
  backlog_item_id   UUID NOT NULL REFERENCES teammy.backlog_items(backlog_item_id) ON DELETE CASCADE,
  added_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (milestone_id, backlog_item_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_milestone_items_backlog
  ON teammy.milestone_items(backlog_item_id);

-- ========== 7) Chat ==========
CREATE TABLE IF NOT EXISTS teammy.chat_sessions (
  chat_session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  type            TEXT NOT NULL DEFAULT 'group' CHECK (type IN ('dm','group','project')),
  group_id        UUID REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  members         INT NOT NULL DEFAULT 0,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_message    TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_chat_project_single
  ON teammy.chat_sessions(group_id) WHERE (type='project');

CREATE TABLE IF NOT EXISTS teammy.chat_session_participants (
  chat_session_id UUID NOT NULL REFERENCES teammy.chat_sessions(chat_session_id) ON DELETE CASCADE,
  user_id         UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  joined_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(chat_session_id, user_id)
);
CREATE INDEX IF NOT EXISTS ix_chat_session_participants_user
  ON teammy.chat_session_participants(user_id);

CREATE TABLE IF NOT EXISTS teammy.messages (
  message_id      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  chat_session_id UUID NOT NULL REFERENCES teammy.chat_sessions(chat_session_id) ON DELETE CASCADE,
  sender_id       UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  type            TEXT,
  content         TEXT NOT NULL,
  is_pinned       BOOLEAN NOT NULL DEFAULT FALSE,
  pinned_at       TIMESTAMPTZ,
  pinned_by       UUID REFERENCES teammy.users(user_id),
  is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
  deleted_at      TIMESTAMPTZ,
  deleted_by      UUID REFERENCES teammy.users(user_id),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_messages_session_created
  ON teammy.messages(chat_session_id, created_at DESC);

-- ========== 8) Activity Logs ==========
CREATE TABLE IF NOT EXISTS teammy.activity_logs (
  activity_id    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id       UUID REFERENCES teammy.groups(group_id) ON DELETE SET NULL,
  entity_type    TEXT NOT NULL,
  entity_id      UUID,
  action         TEXT NOT NULL,
  actor_id       UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  target_user_id UUID REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  message        TEXT,
  metadata       JSONB,
  status         TEXT NOT NULL DEFAULT 'success',
  platform       TEXT,
  severity       TEXT NOT NULL DEFAULT 'info',
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_activity_logs_group
  ON teammy.activity_logs(group_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_activity_logs_actor
  ON teammy.activity_logs(actor_id, created_at DESC);

-- ========== 9) Announcements & Reports ==========
CREATE TABLE IF NOT EXISTS teammy.announcements (
  announcement_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  semester_id     UUID REFERENCES teammy.semesters(semester_id) ON DELETE SET NULL,
  scope           TEXT NOT NULL DEFAULT 'semester' CHECK (scope IN ('global','semester','role','group')),
  target_role     TEXT CHECK (target_role IN ('student','leader','mentor','moderator','admin')),
  target_group_id UUID REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  title           TEXT NOT NULL,
  content         TEXT NOT NULL,
  pinned          BOOLEAN NOT NULL DEFAULT FALSE,
  publish_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  expire_at       TIMESTAMPTZ,
  created_by      UUID NOT NULL REFERENCES teammy.users(user_id)
);

CREATE TABLE IF NOT EXISTS teammy.user_reports (
  report_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  reporter_id UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  target_type TEXT NOT NULL CHECK (target_type IN ('post','message','task_comment','user','file')),
  target_id   UUID NOT NULL,
  semester_id UUID REFERENCES teammy.semesters(semester_id) ON DELETE SET NULL,
  reason      TEXT,
  status      TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','in_progress','resolved','dismissed')),
  assigned_to UUID REFERENCES teammy.users(user_id),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_reports_status ON teammy.user_reports(status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_reports_target ON teammy.user_reports(target_type, target_id);

-- ========== 10) Skill dictionary (optional) ==========
CREATE TABLE IF NOT EXISTS teammy.skill_dictionary (
  token CITEXT PRIMARY KEY,
  role  TEXT NOT NULL,
  major TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS teammy.skill_aliases (
  alias CITEXT PRIMARY KEY,
  token CITEXT NOT NULL REFERENCES teammy.skill_dictionary(token)
);

-- ========== 11) Views / Materialized Views (Fixed) ==========

-- Groups without topic (ADD description to fix g.description error)
CREATE OR REPLACE VIEW teammy.vw_groups_without_topic AS
SELECT g.group_id, g.semester_id, g.major_id, g.name, g.description, g.max_members
FROM teammy.groups g
WHERE g.topic_id IS NULL;

-- Topics available (open & not taken)
DROP MATERIALIZED VIEW IF EXISTS teammy.mv_group_topic_match;

DROP VIEW IF EXISTS teammy.vw_topics_available;
CREATE VIEW teammy.vw_topics_available AS
WITH used AS (
  SELECT topic_id, COUNT(*) AS used_by_groups
  FROM teammy.groups
  WHERE topic_id IS NOT NULL
  GROUP BY topic_id
)
SELECT
  t.topic_id,
  t.semester_id,
  t.major_id,
  t.title,
  t.description,
  t.status,
  t.skills,
  t.source,
  t.source_file_name,
  t.source_file_type,
  t.source_file_size,
  COALESCE(u.used_by_groups,0) AS used_by_groups,
  (t.status='open' AND COALESCE(u.used_by_groups,0)=0) AS can_take_more
FROM teammy.topics t
LEFT JOIN used u ON u.topic_id = t.topic_id
WHERE t.status='open';

-- Students pool (active semester, not in group)
DROP MATERIALIZED VIEW IF EXISTS teammy.mv_students_pool;

CREATE MATERIALIZED VIEW teammy.mv_students_pool AS
SELECT
  u.user_id,
  u.display_name,
  u.major_id,
  s.semester_id,
  CASE
    WHEN u.skills IS NULL THEN NULL
    WHEN jsonb_typeof(u.skills) = 'array' THEN jsonb_build_object(
      'skill_tags',
      COALESCE(
        (
          SELECT jsonb_agg(value)
          FROM jsonb_array_elements(u.skills) AS value
          WHERE value IS NOT NULL AND jsonb_typeof(value) = 'string'
        ),
        '[]'::jsonb
      )
    )
    WHEN jsonb_typeof(u.skills) = 'string' THEN jsonb_build_object('skill_tags', jsonb_build_array(u.skills))
    ELSE u.skills
  END AS skills,
  COALESCE(
    u.skills->>'primary_role',
    u.skills->>'primaryRole',
    u.skills->>'primary'
  ) AS primary_role,
  u.skills_completed
FROM teammy.users u
JOIN teammy.user_roles ur ON ur.user_id = u.user_id
JOIN teammy.roles r ON r.role_id = ur.role_id AND r.name = 'student'
JOIN teammy.semesters s ON s.is_active = TRUE
LEFT JOIN teammy.group_members gm
  ON gm.user_id = u.user_id
  AND gm.semester_id = s.semester_id
  AND gm.status IN ('pending','member','leader')
WHERE gm.group_member_id IS NULL;

-- Group capacity
CREATE MATERIALIZED VIEW IF NOT EXISTS teammy.mv_group_capacity AS
SELECT
  g.group_id, g.semester_id, g.major_id, g.name, g.description,
  g.max_members,
  COUNT(gm.group_member_id) FILTER (WHERE gm.status IN ('pending','member','leader')) AS current_members,
  GREATEST(g.max_members - COUNT(gm.group_member_id) FILTER (WHERE gm.status IN ('pending','member','leader')), 0)
    AS remaining_slots
FROM teammy.groups g
LEFT JOIN teammy.group_members gm ON gm.group_id = g.group_id
GROUP BY g.group_id, g.semester_id, g.major_id, g.name, g.description, g.max_members;

-- Group-Topic simple match (NOW FIXED: g.description is present in vw_groups_without_topic)
DROP MATERIALIZED VIEW IF EXISTS teammy.mv_group_topic_match;
CREATE MATERIALIZED VIEW teammy.mv_group_topic_match AS
SELECT 
  g.group_id, g.semester_id, g.major_id, g.name AS group_name, g.description AS group_desc,
  t.topic_id, t.title, t.description AS topic_desc,
  (CASE WHEN g.major_id = t.major_id THEN 1 ELSE 0 END)
  + (CASE WHEN position(lower(split_part(t.title,' ',1)) in lower(coalesce(g.description,''))) > 0 THEN 1 ELSE 0 END)
  AS simple_score
FROM teammy.vw_groups_without_topic g
JOIN teammy.vw_topics_available t
  ON t.semester_id = g.semester_id AND (t.major_id = g.major_id OR t.major_id IS NULL);

-- Unique indexes for CONCURRENT refresh
CREATE UNIQUE INDEX IF NOT EXISTS ux_mv_students_pool_user ON teammy.mv_students_pool(user_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_mv_group_capacity_group ON teammy.mv_group_capacity(group_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_mv_gtm_pair ON teammy.mv_group_topic_match(group_id, topic_id);



-- Roles
INSERT INTO teammy.roles (name) VALUES
  ('admin'), ('moderator'), ('mentor'), ('student')
ON CONFLICT (name) DO NOTHING;


-- Semester (active)
INSERT INTO teammy.semesters (season, year, start_date, end_date, is_active)
VALUES ('Fall', extract(year from CURRENT_DATE)::int, CURRENT_DATE - 7, CURRENT_DATE + 90, TRUE)
ON CONFLICT DO NOTHING;

-- Ensure active semester has policy windows for AI guardrails
INSERT INTO teammy.semester_policy (
  semester_id,
  team_self_select_start,
  team_self_select_end,
  team_suggest_start,
  topic_self_select_start,
  topic_self_select_end,
  topic_suggest_start,
  desired_group_size_min,
  desired_group_size_max)
SELECT s.semester_id,
       CURRENT_DATE - 21,
       CURRENT_DATE + 45,
       CURRENT_DATE - 7,
       CURRENT_DATE - 14,
       CURRENT_DATE + 60,
       CURRENT_DATE - 7,
       4,
       6
FROM teammy.semesters s
WHERE s.is_active = TRUE
ON CONFLICT (semester_id) DO UPDATE
SET team_self_select_start = EXCLUDED.team_self_select_start,
    team_self_select_end = EXCLUDED.team_self_select_end,
    team_suggest_start = EXCLUDED.team_suggest_start,
    topic_self_select_start = EXCLUDED.topic_self_select_start,
    topic_self_select_end = EXCLUDED.topic_self_select_end,
    topic_suggest_start = EXCLUDED.topic_suggest_start,
    desired_group_size_min = EXCLUDED.desired_group_size_min,
    desired_group_size_max = EXCLUDED.desired_group_size_max;

-- Majors seed data powering skills & AI context
INSERT INTO teammy.majors (major_id, major_name) VALUES
  ('11111111-1111-1111-1111-111111111111', 'Software Engineering'),
  ('22222222-2222-2222-2222-222222222222', 'Information Systems'),
  ('33333333-3333-3333-3333-333333333333', 'Digital Product Design')
ON CONFLICT (major_name) DO NOTHING;

-- Sample students with JSON skills to populate mv_students_pool
INSERT INTO teammy.users (user_id, email, email_verified, display_name, major_id, skills, skills_completed)
VALUES
  ('aaaa1111-1111-1111-1111-aaaaaaaa0001', 'alice@teammy.dev', TRUE, 'Alice Nguyen', '11111111-1111-1111-1111-111111111111',
   $$
   {
     "primary_role": "frontend",
     "skill_tags": ["react","typescript","figma","tailwind"],
     "soft_skills": ["teamwork","presentation"]
   }
   $$::jsonb, TRUE),
  ('bbbb1111-1111-1111-1111-bbbbbbbb0002', 'bao@teammy.dev', TRUE, 'Bao Tran', '11111111-1111-1111-1111-111111111111',
   $$
   {
     "primary_role": "backend",
     "skill_tags": ["dotnet","postgres","azure","redis"],
     "certifications": ["az-204"]
   }
   $$::jsonb, TRUE),
  ('cccc1111-1111-1111-1111-cccccccc0003', 'chi@teammy.dev', TRUE, 'Chi Le', '22222222-2222-2222-2222-222222222222',
   $$
   {
     "primary_role": "backend",
     "skill_tags": ["python","data","etl","sql"],
     "interests": ["bi","analytics"]
   }
   $$::jsonb, TRUE),
  ('dddd1111-1111-1111-1111-dddddddd0004', 'dung@teammy.dev', TRUE, 'Dung Vo', '33333333-3333-3333-3333-333333333333',
   $$
   {
     "primary_role": "frontend",
     "skill_tags": ["ux","figma","illustrator","motion"],
     "soft_skills": ["storytelling"]
   }
   $$::jsonb, TRUE)
ON CONFLICT (email) DO UPDATE
SET display_name = EXCLUDED.display_name,
    major_id = EXCLUDED.major_id,
    skills = EXCLUDED.skills,
    skills_completed = EXCLUDED.skills_completed,
    updated_at = now();

-- Assign student role
INSERT INTO teammy.user_roles (user_id, role_id)
SELECT u.user_id, r.role_id
FROM teammy.users u
JOIN teammy.roles r ON r.name = 'student'
WHERE u.email IN ('alice@teammy.dev','bao@teammy.dev','chi@teammy.dev','dung@teammy.dev')
ON CONFLICT (user_id, role_id) DO NOTHING;

-- Skills JSON for sample users
UPDATE teammy.users SET skills = $$
  {
    "primary_role": "frontend",
    "skill_tags": ["react","typescript","tailwind","uiux","signalr"],
    "portfolio": "https://dribbble.com/alice",
    "stack": ["react","nextjs","tailwind"]
  }
$$::jsonb,
skills_completed = TRUE
WHERE email = 'alice@teammy.dev';

UPDATE teammy.users SET skills = $$
  {
    "primary_role": "backend",
    "skill_tags": ["dotnet","postgres","redis","grpc","clean-architecture"],
    "certifications": ["Azure-204","AWS-Dev"]
  }
$$::jsonb,
skills_completed = TRUE
WHERE email = 'bao@teammy.dev';

UPDATE teammy.users SET skills = $$
  {
    "primaryRole": "backend",
    "skills": ["python","sql","etl","dbt","powerbi"],
    "interests": ["data engineering","analytics"]
  }
$$::jsonb,
skills_completed = TRUE
WHERE email = 'chi@teammy.dev';

UPDATE teammy.users SET skills = $$
  {
    "primaryRole": "frontend",
    "skill_tags": ["flutter","dart","figma","motion-design"],
    "stack": ["flutter","firebase"]
  }
$$::jsonb,
skills_completed = TRUE
WHERE email = 'dung@teammy.dev';


-- Sample groups used by AI flows
WITH active_semester AS (
  SELECT semester_id FROM teammy.semesters WHERE is_active = TRUE LIMIT 1
),
se_major AS (
  SELECT major_id FROM teammy.majors WHERE major_name = 'Software Engineering'
),
is_major AS (
  SELECT major_id FROM teammy.majors WHERE major_name = 'Information Systems'
)
INSERT INTO teammy.groups (group_id, semester_id, major_id, name, description, max_members, status)
SELECT 'aaaa2222-2222-2222-2222-aaaaaaaa0001', s.semester_id, se.major_id,
     'Alpha Builders', 'Nhóm xây dựng nền tảng cộng tác realtime cho sinh viên CNTT.', 6, 'recruiting'
FROM active_semester s CROSS JOIN se_major se
WHERE NOT EXISTS (SELECT 1 FROM teammy.groups WHERE group_id = 'aaaa2222-2222-2222-2222-aaaaaaaa0001')
UNION ALL
SELECT 'bbbb2222-2222-2222-2222-bbbbbbbb0002', s.semester_id, isj.major_id,
     'Insight Squad', 'Nhóm dữ liệu tập trung vào dashboard phân tích học tập.', 5, 'recruiting'
FROM active_semester s CROSS JOIN is_major isj
WHERE NOT EXISTS (SELECT 1 FROM teammy.groups WHERE group_id = 'bbbb2222-2222-2222-2222-bbbbbbbb0002');

-- Leaders/members for seeded groups (keeps Alice & Dung free for mv_students_pool)
WITH alpha_group AS (
  SELECT group_id, semester_id FROM teammy.groups WHERE group_id = 'aaaa2222-2222-2222-2222-aaaaaaaa0001'
),
alpha_lead AS (
  SELECT user_id FROM teammy.users WHERE email = 'bao@teammy.dev'
)
INSERT INTO teammy.group_members (group_member_id, group_id, user_id, semester_id, status)
SELECT gen_random_uuid(), g.group_id, u.user_id, g.semester_id, 'leader'
FROM alpha_group g CROSS JOIN alpha_lead u
WHERE NOT EXISTS (
  SELECT 1 FROM teammy.group_members WHERE group_id = g.group_id AND user_id = u.user_id);

WITH insight_group AS (
  SELECT group_id, semester_id FROM teammy.groups WHERE group_id = 'bbbb2222-2222-2222-2222-bbbbbbbb0002'
),
insight_lead AS (
  SELECT user_id FROM teammy.users WHERE email = 'chi@teammy.dev'
)
INSERT INTO teammy.group_members (group_member_id, group_id, user_id, semester_id, status)
SELECT gen_random_uuid(), g.group_id, u.user_id, g.semester_id, 'leader'
FROM insight_group g CROSS JOIN insight_lead u
WHERE NOT EXISTS (
  SELECT 1 FROM teammy.group_members WHERE group_id = g.group_id AND user_id = u.user_id);

-- Open topics with keyword-rich descriptions for topic matching
WITH active_semester AS (
  SELECT semester_id FROM teammy.semesters WHERE is_active = TRUE LIMIT 1
),
se_major AS (
  SELECT major_id FROM teammy.majors WHERE major_name = 'Software Engineering'
),
is_major AS (
  SELECT major_id FROM teammy.majors WHERE major_name = 'Information Systems'
),
creator AS (
  SELECT user_id FROM teammy.users WHERE email = 'bao@teammy.dev'
)
INSERT INTO teammy.topics (topic_id, semester_id, major_id, title, description, status, created_by)
SELECT 'aaaa3333-3333-3333-3333-aaaaaaaa0001', s.semester_id, se.major_id,
     'Realtime Collaboration Hub',
     'Xây dựng hub realtime với SignalR, React, TypeScript và Redis cache để đồng bộ bảng Kanban.',
     'open', c.user_id
FROM active_semester s CROSS JOIN se_major se CROSS JOIN creator c
WHERE NOT EXISTS (SELECT 1 FROM teammy.topics WHERE topic_id = 'aaaa3333-3333-3333-3333-aaaaaaaa0001')
UNION ALL
SELECT 'bbbb3333-3333-3333-3333-bbbbbbbb0002', s.semester_id, isj.major_id,
     'InsightOps Dashboard',
     'Pipeline phân tích dữ liệu học tập với Python, dbt, PowerBI và trực quan hoá realtime.',
     'open', c.user_id
FROM active_semester s CROSS JOIN is_major isj CROSS JOIN creator c
WHERE NOT EXISTS (SELECT 1 FROM teammy.topics WHERE topic_id = 'bbbb3333-3333-3333-3333-bbbbbbbb0002');

-- Recruitment posts referencing seeded groups + JSON skill requirements
WITH alpha_group AS (
  SELECT g.group_id, g.semester_id, g.major_id FROM teammy.groups g
  WHERE g.group_id = 'aaaa2222-2222-2222-2222-aaaaaaaa0001'
),
insight_group AS (
  SELECT g.group_id, g.semester_id, g.major_id FROM teammy.groups g
  WHERE g.group_id = 'bbbb2222-2222-2222-2222-bbbbbbbb0002'
)
INSERT INTO teammy.recruitment_posts (
  post_id, semester_id, post_type, group_id, major_id, title, description,
  position_needed, required_skills, status, application_deadline)
SELECT 'aaaa4444-4444-4444-4444-aaaaaaaa0001', ag.semester_id, 'group_hiring', ag.group_id, ag.major_id,
     'Alpha Builders cần Frontend React',
     'Tuyển bạn FE build UI realtime: React, TypeScript, Tailwind, SignalR.',
     'Frontend React / UI-UX',
     $$
     {
     "primary_role": "frontend",
     "skill_tags": ["react","typescript","tailwind","signalr","uiux"]
     }
     $$::jsonb,
     'open', now() + interval '14 days'
FROM alpha_group ag
WHERE NOT EXISTS (SELECT 1 FROM teammy.recruitment_posts WHERE post_id = 'aaaa4444-4444-4444-4444-aaaaaaaa0001')
UNION ALL
SELECT 'bbbb4444-4444-4444-4444-bbbbbbbb0002', ig.semester_id, 'group_hiring', ig.group_id, ig.major_id,
     'Insight Squad tuyển Data Wrangler',
     'Cần thành viên ETL/analytics: Python, dbt, SQL, dashboard PowerBI.',
     'Data & Backend (Python/SQL)',
     $$
     {
     "primary_role": "backend",
     "skill_tags": ["python","sql","dbt","powerbi","etl"]
     }
     $$::jsonb,
     'open', now() + interval '10 days'
FROM insight_group ig
WHERE NOT EXISTS (SELECT 1 FROM teammy.recruitment_posts WHERE post_id = 'bbbb4444-4444-4444-4444-bbbbbbbb0002');
