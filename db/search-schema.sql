-- ---------------------------------------------------------------------------
-- search schema — the read model owned by the Search API (Dapper).
--
-- This is a denormalised projection of post.job_postings + post.managers,
-- built for exactly two queries: the filtered/paged listing and the detail
-- page by slug. It is written only by POST /internal/projections/job and is
-- never joined to the post schema.
--
-- Idempotent: safe to run on every startup and on every compose init.
-- ---------------------------------------------------------------------------

create schema if not exists search;

-- array_to_string() is only STABLE, so it cannot be used directly in a
-- generated column. This wrapper is IMMUTABLE for the text[] → text case we
-- need, which lets skills participate in full-text search.
create or replace function search.skills_to_text(skills text[])
    returns text
    language sql
    immutable
    parallel safe
as $$
    select coalesce(array_to_string(skills, ' '), '');
$$;

create table if not exists search.job_listings (
    id                 uuid          primary key,
    slug               text          not null unique,
    title              text          not null,
    department         text          not null,
    location           text          not null,
    country            text          not null,
    work_arrangement   text          not null,
    employment_type    text          not null,
    seniority          text          not null,
    salary_min         numeric(12,2) not null,
    salary_max         numeric(12,2) not null,
    salary_currency    char(3)       not null,
    pay_period         text          not null,
    salary_visible     boolean       not null,
    description        text          not null,
    responsibilities   text,
    requirements       text,
    skills             text[]        not null default '{}',
    organization       text          not null,   -- copied from the manager; intentional denormalisation
    application_url    text,
    application_email  text,
    closing_date       date          not null,
    published_at       timestamptz   not null,
    -- true while the posting's status is Published. Expiry (closing_date in the
    -- past) is evaluated at query time because current_date is not immutable.
    is_open            boolean       not null,
    -- Monotonic version from the write side. The projection endpoint discards any
    -- message whose version is not greater than this, making apply idempotent.
    version            integer       not null,
    projected_at       timestamptz   not null default now(),
    search_vector      tsvector      generated always as (
        setweight(to_tsvector('english', coalesce(title, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(department, '') || ' ' || coalesce(organization, '') || ' ' || coalesce(location, '')), 'B') ||
        setweight(to_tsvector('english', search.skills_to_text(skills)), 'B') ||
        setweight(to_tsvector('english', coalesce(description, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(responsibilities, '') || ' ' || coalesce(requirements, '')), 'D')
    ) stored
);

-- Keyword search: ?q= is matched with @@ against the weighted tsvector.
create index if not exists ix_job_listings_search_vector
    on search.job_listings using gin (search_vector);

-- Skill filtering with the array containment operator (skills @> '{...}').
create index if not exists ix_job_listings_skills
    on search.job_listings using gin (skills);

-- The default board: open listings, newest first, keyset-paged on published_at.
create index if not exists ix_job_listings_open_published
    on search.job_listings (is_open, published_at desc);

-- Facet filters and facet counts.
create index if not exists ix_job_listings_department
    on search.job_listings (department);
create index if not exists ix_job_listings_work_arrangement
    on search.job_listings (work_arrangement);
create index if not exists ix_job_listings_employment_type
    on search.job_listings (employment_type);

-- Excludes expired postings (closing_date < current_date) in every listing query.
create index if not exists ix_job_listings_closing_date
    on search.job_listings (closing_date);
