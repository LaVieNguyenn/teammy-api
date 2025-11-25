
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
  skills           JSONB,
  skills_completed BOOLEAN NOT NULL DEFAULT FALSE,
  is_active        BOOLEAN NOT NULL DEFAULT TRUE,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);

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
  status       TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','closed','archived')),
  created_by   UUID NOT NULL REFERENCES teammy.users(user_id),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (semester_id, title)
);

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
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (semester_id, name)
);

CREATE TABLE IF NOT EXISTS teammy.group_members (
  group_member_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id        UUID NOT NULL REFERENCES teammy.groups(group_id) ON DELETE CASCADE,
  user_id         UUID NOT NULL REFERENCES teammy.users(user_id) ON DELETE CASCADE,
  semester_id     UUID NOT NULL REFERENCES teammy.semesters(semester_id),
  status          TEXT NOT NULL CHECK (status IN ('pending','member','leader','left','removed','failed','completed')),
  joined_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  left_at         TIMESTAMPTZ
);

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
  expires_at      TIMESTAMPTZ,
  CONSTRAINT ux_invite_active UNIQUE (group_id, invitee_user_id)
);

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
  file_url    TEXT NOT NULL,
  file_type   TEXT,
  file_size   BIGINT,
  description TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

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
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_messages_session_created
  ON teammy.messages(chat_session_id, created_at DESC);

-- ========== 8) Announcements & Reports ==========
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

-- ========== 9) Skill dictionary (optional) ==========
CREATE TABLE IF NOT EXISTS teammy.skill_dictionary (
  token CITEXT PRIMARY KEY,
  role  TEXT NOT NULL,
  major TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS teammy.skill_aliases (
  alias CITEXT PRIMARY KEY,
  token CITEXT NOT NULL REFERENCES teammy.skill_dictionary(token)
);

-- ========== 10) Views / Materialized Views (Fixed) ==========

-- Groups without topic (ADD description to fix g.description error)
CREATE OR REPLACE VIEW teammy.vw_groups_without_topic AS
SELECT g.group_id, g.semester_id, g.major_id, g.name, g.description, g.max_members
FROM teammy.groups g
WHERE g.topic_id IS NULL;

-- Topics available (open & not taken)
CREATE OR REPLACE VIEW teammy.vw_topics_available AS
WITH used AS (
  SELECT topic_id, COUNT(*) AS used_by_groups
  FROM teammy.groups
  WHERE topic_id IS NOT NULL
  GROUP BY topic_id
)
SELECT
  t.topic_id, t.semester_id, t.major_id, t.title, t.description, t.status,
  COALESCE(u.used_by_groups,0) AS used_by_groups,
  (t.status='open' AND COALESCE(u.used_by_groups,0)=0) AS can_take_more
FROM teammy.topics t
LEFT JOIN used u ON u.topic_id = t.topic_id
WHERE t.status='open';

-- Students pool (active semester, not in group)
CREATE MATERIALIZED VIEW IF NOT EXISTS teammy.mv_students_pool AS
SELECT u.user_id, u.display_name, u.major_id, s.semester_id,
       u.skills, COALESCE(u.skills->>'primary_role','') AS primary_role,
       u.skills_completed
FROM teammy.users u
JOIN teammy.user_roles ur ON ur.user_id=u.user_id
JOIN teammy.roles r ON r.role_id=ur.role_id AND r.name='student'
JOIN teammy.semesters s ON s.is_active=TRUE
LEFT JOIN teammy.group_members gm
  ON gm.user_id=u.user_id AND gm.semester_id=s.semester_id AND gm.status IN ('pending','member','leader')
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
CREATE MATERIALIZED VIEW IF NOT EXISTS teammy.mv_group_topic_match AS
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



