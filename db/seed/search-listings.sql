-- ---------------------------------------------------------------------------
-- Demo/perf seed for the READ model only: ~1,000 varied open listings plus a
-- handful of closed and expired ones. Deterministic (seeded random) so the
-- board looks the same on every machine. Safe to re-run: rows are keyed by a
-- fixed UUID derived from the series number and upserted.
--
-- These rows exist only in search.job_listings — they have no counterpart in
-- the post schema and no manager can edit them. They are here so the public
-- board has something to show and so query plans can be judged at a realistic
-- size (CLAUDE.md phase 6: "query performance sane against ~1,000 seeded rows").
-- ---------------------------------------------------------------------------
select setseed(0.42);

with
titles as (select array[
    'Warehouse Supervisor','Fleet Maintenance Technician','Logistics Data Analyst','HR Business Partner','Night Shift Loader',
    'Inbound Shift Lead','Dispatch Supervisor','Transport Planner','Customer Operations Specialist','Finance Analyst',
    'Payroll Administrator','Software Engineer','Platform Engineer','Data Engineer','Product Manager','QA Analyst',
    'Site Operations Manager','Health & Safety Advisor','Recruitment Coordinator','Procurement Lead',
    'Forklift Operator','Route Optimisation Analyst','Warehouse Team Leader','Facilities Coordinator','IT Support Technician'
  ] as v),
prefixes as (select array['','Senior ','Junior ','Lead ','Principal ',''] as v),
departments as (select array['Operations','Operations','Operations','Data & Insight','People','Finance','Engineering','Transport','Customer Service','Facilities'] as v),
locations as (select array[
    'Leeds, West Yorkshire','Rotherham, South Yorkshire','Doncaster, South Yorkshire','Manchester','Birmingham','Bristol',
    'Glasgow','Cardiff','Toronto, ON','Vancouver, BC','Calgary, AB','Montréal, QC','Remote (UK)','Remote (Canada)'
  ] as v),
countries as (select array['United Kingdom','United Kingdom','United Kingdom','United Kingdom','United Kingdom','United Kingdom',
    'United Kingdom','United Kingdom','Canada','Canada','Canada','Canada','United Kingdom','Canada'] as v),
arrangements as (select array['OnSite','OnSite','Hybrid','Hybrid','Remote'] as v),
types as (select array['FullTime','FullTime','FullTime','PartTime','Contract','Temporary','Internship'] as v),
seniorities as (select array['Intern','Junior','Mid','Mid','Senior','Senior','Lead','Principal','Director'] as v),
orgs as (select array['Northline Logistics','Northline Logistics','Northline Logistics','Harbourfront Freight','Pennine Foods','Maple Ridge Transport','Ayrton Systems'] as v),
skillpool as (select array['Warehouse ops','WMS','Team leadership','H&S','Shift planning','Forklift','SQL','Python','Excel','Power BI',
    'C#','PostgreSQL','Kubernetes','Angular','TypeScript','Recruitment','Payroll','SAP','Route planning','Customer service',
    'IOSH','Lean','Six Sigma','Fleet management','Stock control'] as v),
seq as (select generate_series(1, 1000) as n),
rows as (
  select
    n,
    md5('seed-listing-' || n)::uuid                                   as id,
    (select v from prefixes)[1 + (random() * 5)::int] || (select v from titles)[1 + (random() * 24)::int] as title,
    (select v from departments)[1 + (random() * 9)::int]             as department,
    1 + (random() * 13)::int                                          as loc_idx,
    (select v from arrangements)[1 + (random() * 4)::int]            as work_arrangement,
    (select v from types)[1 + (random() * 6)::int]                   as employment_type,
    (select v from seniorities)[1 + (random() * 8)::int]             as seniority,
    (22000 + (random() * 60000)::int / 500 * 500)::numeric(12,2)     as salary_min,
    (select v from orgs)[1 + (random() * 6)::int]                     as organization,
    random() < 0.85                                                   as salary_visible,
    now() - (random() * 45 || ' days')::interval                      as published_at,
    (random() * 60)::int                                              as days_to_close,
    random()                                                          as r
  from seq
)
insert into search.job_listings (
    id, slug, title, department, location, country, work_arrangement, employment_type, seniority,
    salary_min, salary_max, salary_currency, pay_period, salary_visible,
    description, responsibilities, requirements, skills, organization,
    application_url, application_email, closing_date, published_at, is_open, version, projected_at)
select
    id,
    lower(regexp_replace(title || '-' || split_part((select v from locations)[loc_idx], ',', 1) || '-' || n, '[^a-zA-Z0-9]+', '-', 'g')),
    title,
    department,
    (select v from locations)[loc_idx],
    (select v from countries)[loc_idx],
    work_arrangement,
    employment_type,
    seniority,
    -- Hourly roles get hourly-scale numbers (roughly annual ÷ 2,000).
    case when employment_type in ('PartTime','Temporary') and r < 0.5 then round(salary_min / 2000) else salary_min end,
    case when employment_type in ('PartTime','Temporary') and r < 0.5
         then round((salary_min + 4000 + (r * 20000)::int / 500 * 500) / 2000)
         else salary_min + 4000 + (r * 20000)::int / 500 * 500 end,
    case when (select v from countries)[loc_idx] = 'Canada' then 'CAD' else 'GBP' end,
    case when employment_type in ('PartTime','Temporary') and r < 0.5 then 'Hourly' else 'Annual' end,
    salary_visible,
    organization || ' is hiring a ' || title || ' in ' || (select v from locations)[loc_idx] || '. '
      || 'You will join the ' || department || ' team on a ' || employment_type || ' basis, working '
      || work_arrangement || '. The role owns day-to-day delivery, reporting and continuous improvement, '
      || 'and partners closely with site leadership, transport planning and customer operations to hit '
      || 'service, safety and cost targets. We offer structured onboarding, clear progression and a supportive team.',
    'Own the daily plan and communicate it clearly' || E'\n' || 'Coach and develop the team' || E'\n' || 'Report on KPIs weekly' || E'\n' || 'Drive continuous improvement',
    '2+ years in a similar role' || E'\n' || 'Comfortable with data and systems' || E'\n' || 'Right to work in ' || (select v from countries)[loc_idx],
    (select array_agg(distinct s) from unnest(array[
        (select v from skillpool)[1 + (random() * 24)::int],
        (select v from skillpool)[1 + (random() * 24)::int],
        (select v from skillpool)[1 + (random() * 24)::int],
        (select v from skillpool)[1 + (random() * 24)::int]
    ]) as s),
    organization,
    case when r < 0.6 then 'https://careers.example.com/apply/' || n else null end,
    case when r >= 0.6 then 'careers@' || lower(regexp_replace(organization, '[^a-zA-Z]', '', 'g')) || '.example' else null end,
    -- ~4% closed (is_open=false), ~4% expired (closing date in the past), rest open
    case when r < 0.04 then current_date - 5
         when r >= 0.04 and r < 0.08 then current_date - (1 + (random() * 30)::int)
         else current_date + 1 + days_to_close end,
    published_at,
    r >= 0.04,
    1,
    now()
from rows
on conflict (id) do update set
    title = excluded.title, department = excluded.department, location = excluded.location, country = excluded.country,
    work_arrangement = excluded.work_arrangement, employment_type = excluded.employment_type, seniority = excluded.seniority,
    salary_min = excluded.salary_min, salary_max = excluded.salary_max, salary_currency = excluded.salary_currency,
    pay_period = excluded.pay_period, salary_visible = excluded.salary_visible, description = excluded.description,
    responsibilities = excluded.responsibilities, requirements = excluded.requirements, skills = excluded.skills,
    organization = excluded.organization, application_url = excluded.application_url, application_email = excluded.application_email,
    closing_date = excluded.closing_date, published_at = excluded.published_at, is_open = excluded.is_open,
    version = excluded.version, projected_at = now();

analyze search.job_listings;
