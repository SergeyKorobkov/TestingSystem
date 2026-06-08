-- PostgreSQL / Postgres Pro schema + demo data for TestSystemCoursework_Full
-- Run in pgAdmin: Query Tool -> paste -> Execute.

create table if not exists users (
  id serial primary key,
  login text not null unique,
  password_hash text not null,
  role text not null,
  max_attempts int not null
);

create table if not exists tests (
  id serial primary key,
  title text not null,
  description text null
);

create table if not exists questions (
  id serial primary key,
  test_id int not null references tests(id) on delete cascade,
  text text not null,
  kind text not null check (kind in ('single','multiple')),
  weight int not null default 1,
  image_path text null
);

create table if not exists options (
  id serial primary key,
  question_id int not null references questions(id) on delete cascade,
  text text not null,
  is_correct boolean not null default false
);

-- idempotent demo data
insert into users(login, password_hash, role, max_attempts)
values
  ('admin', 'admin', 'admin', 5),
  ('student', 'student', 'student', 3)
on conflict (login) do nothing;

insert into tests(title, description)
values
  ('C# Basics', 'Intro quiz'),
  ('SQL Basics', 'SELECT / JOIN / GROUP BY')
on conflict do nothing;

-- Demo questions/options for the first test (id=1) if present
do $$
declare
  t_id int;
  q_id int;
begin
  select id into t_id from tests where title='C# Basics' limit 1;
  if t_id is not null then
    insert into questions(test_id, text, kind, weight)
    values (t_id, 'What does "static" mean for a method?', 'single', 1)
    returning id into q_id;

    insert into options(question_id, text, is_correct) values
      (q_id, 'The method belongs to the type (class), not an instance', true),
      (q_id, 'The method can only be called once', false),
      (q_id, 'The method cannot have parameters', false);
  end if;
exception when others then
  -- ignore if already inserted
  null;
end $$;
