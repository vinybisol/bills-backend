# Schema

Definição completa em `docs/schema.sql`. Resumo das tabelas e pontos-chave.

## Tabelas
- **app_user**(id, name, email UNIQUE, firebase_uid UNIQUE, created_at) — identidade própria; provisionado no 1º login.
- **person**(id, owner_id→app_user, name, app_user_id→app_user NULL, active, created_at) — `app_user_id` NULL=rótulo; preenchido=acesso (Fase 2).
- **category**(id, owner_id, name, active, created_at) — UNIQUE(owner_id,name).
- **bill**(id, owner_id, name, category_id, kind['recurring'|'one_off'], default_amount, split_ratio[0..1], person_id NULL, active, created_at).
- **income**(id, owner_id, name, kind, default_amount, active, created_at).
- **bill_entry**(id, owner_id, bill_id, ref_year, ref_month, planned_amount, actual_amount, split_ratio_snapshot, paid, paid_date, person_id, received, received_date, created_at) — UNIQUE(bill_id,ref_year,ref_month).
- **income_entry**(id, owner_id, income_id, ref_year, ref_month, planned_amount, actual_amount, received, received_date, created_at) — UNIQUE(income_id,ref_year,ref_month).

## Invariantes no schema
- `owner_id` é FK para `app_user.id` (BIGINT), **nunca** o firebase_uid.
- `planned_amount` e `split_ratio_snapshot` nos `*_entry` são **cópias** (snapshot), não FK — base da imutabilidade.
- UNIQUE por (molde, ano, mês) impede lançamento duplicado e dá idempotência à projeção.
- Moldes: soft delete via `active`.
- `split_ratio`/`split_ratio_snapshot`: NUMERIC(5,4), CHECK 0..1.
- Valores monetários: NUMERIC(12,2).

## Queries-chave (em schema.sql)
1. Projeção anual (CROSS JOIN generate_series(1,12) + ON CONFLICT DO NOTHING).
2. Recálculo (UPDATE planned_amount WHERE paid=false AND período >= from).
3. Painel a-receber por pessoa (GROUP BY person, split_snapshot < 1).
