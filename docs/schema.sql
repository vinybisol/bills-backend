-- ============================================================
-- Sistema de Orçamento Pessoal — Schema PostgreSQL (Neon)
-- Identidade própria (app_user) desacoplada do provedor (Firebase).
-- owner_id em todo o domínio aponta para app_user.id (BIGINT interno),
-- NUNCA para o uid do Firebase. O provedor fica isolado em app_user.
-- ============================================================

-- ------------------------------------------------------------
-- APP_USER — Identidade própria do sistema
-- PK interna (id) é a verdadeira identidade usada em todo o domínio.
-- firebase_uid é apenas o vínculo com o provedor atual de auth,
-- isolado nesta tabela. Trocar de provedor = mexer só nesta coluna.
-- Usuários NÃO são cadastrados por tela: nascem por provisionamento
-- just-in-time no primeiro login (a API cria se o firebase_uid é novo).
-- ------------------------------------------------------------
CREATE TABLE app_user (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name          TEXT NOT NULL,
    email         TEXT UNIQUE,
    firebase_uid  TEXT UNIQUE,                    -- vínculo trocável com o provedor
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_appuser_firebase ON app_user(firebase_uid) WHERE firebase_uid IS NOT NULL;

-- ------------------------------------------------------------
-- PERSON — Quem deve diferenças de contas compartilhadas
-- CRUD normal (tela de cadastro), escopado por owner_id.
-- app_user_id NULL  = apenas um rótulo (Fase 1, esposa só acompanhada)
-- app_user_id != NULL = pessoa vinculada a um acesso (Fase 2+)
-- O vínculo aponta para app_user (identidade própria), nunca pro Firebase.
-- ------------------------------------------------------------
CREATE TABLE person (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id      BIGINT NOT NULL REFERENCES app_user(id),
    name          TEXT NOT NULL,
    app_user_id   BIGINT REFERENCES app_user(id),   -- NULL = rótulo; preenchido = tem acesso
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_person_owner   ON person(owner_id);
CREATE INDEX idx_person_appuser ON person(app_user_id) WHERE app_user_id IS NOT NULL;

-- ------------------------------------------------------------
-- CATEGORY — Agrupamento de gastos (moradia, saúde, lazer...)
-- ------------------------------------------------------------
CREATE TABLE category (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id    BIGINT NOT NULL REFERENCES app_user(id),
    name        TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (owner_id, name)
);
CREATE INDEX idx_category_owner ON category(owner_id);

-- ------------------------------------------------------------
-- BILL — Molde da despesa
-- Define O QUE é a conta, não um valor de um mês específico.
-- recurring = projeta o ano todo; one_off = avulsa (IPVA, revisão).
-- split_ratio = fração que é EFETIVAMENTE minha:
--   1.0 = só minha | 0.5 = dividida 50/50 | 0.0 = passa por mim (telefone)
-- person_id = quem deve a diferença (NULL quando split_ratio = 1.0)
-- ------------------------------------------------------------
CREATE TABLE bill (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id        BIGINT NOT NULL REFERENCES app_user(id),
    name            TEXT NOT NULL,
    category_id     BIGINT REFERENCES category(id),
    kind            TEXT NOT NULL CHECK (kind IN ('recurring','one_off')),
    default_amount  NUMERIC(12,2) NOT NULL DEFAULT 0,   -- valor previsto padrão
    split_ratio     NUMERIC(5,4) NOT NULL DEFAULT 1.0
                        CHECK (split_ratio >= 0 AND split_ratio <= 1),
    person_id       BIGINT REFERENCES person(id),
    active          BOOLEAN NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_bill_owner ON bill(owner_id);

-- ------------------------------------------------------------
-- INCOME — Molde da receita
-- Salário, freela (Rodotoss), etc. Mesma mecânica do bill.
-- ------------------------------------------------------------
CREATE TABLE income (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id        BIGINT NOT NULL REFERENCES app_user(id),
    name            TEXT NOT NULL,
    kind            TEXT NOT NULL CHECK (kind IN ('recurring','one_off')),
    default_amount  NUMERIC(12,2) NOT NULL DEFAULT 0,
    active          BOOLEAN NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_income_owner ON income(owner_id);

-- ------------------------------------------------------------
-- BILL_ENTRY — Lançamento mensal de despesa
-- planned_amount = SNAPSHOT do previsto (cópia, nunca FK ao molde).
--   É o que garante imutabilidade: editar o bill não reescreve o passado.
-- actual_amount = valor real pago.
-- paid + paid_date = estado de pagamento ("a pagar" vs "paga").
-- A RECEBER vive aqui (sem entidade separada):
--   person_id, received, received_date.
--   valor a receber = actual_amount * (1 - split_ratio_snapshot)
-- split_ratio_snapshot = cópia do split no momento do lançamento.
-- ------------------------------------------------------------
CREATE TABLE bill_entry (
    id                    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id              BIGINT NOT NULL REFERENCES app_user(id),
    bill_id               BIGINT NOT NULL REFERENCES bill(id),
    ref_year              SMALLINT NOT NULL,
    ref_month             SMALLINT NOT NULL CHECK (ref_month BETWEEN 1 AND 12),

    planned_amount        NUMERIC(12,2) NOT NULL,        -- snapshot do previsto
    actual_amount         NUMERIC(12,2),                 -- real (NULL até pagar)
    split_ratio_snapshot  NUMERIC(5,4) NOT NULL,         -- snapshot do split

    paid                  BOOLEAN NOT NULL DEFAULT false,
    paid_date             DATE,

    -- a receber (derivado): só relevante quando split_ratio_snapshot < 1
    person_id             BIGINT REFERENCES person(id),
    received              BOOLEAN NOT NULL DEFAULT false,
    received_date         DATE,

    created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (bill_id, ref_year, ref_month)                -- 1 lançamento por mês
);
CREATE INDEX idx_billentry_owner_period ON bill_entry(owner_id, ref_year, ref_month);
CREATE INDEX idx_billentry_person       ON bill_entry(person_id) WHERE person_id IS NOT NULL;

-- ------------------------------------------------------------
-- INCOME_ENTRY — Lançamento mensal de receita
-- Mesma lógica de snapshot e estado (received = recebido).
-- ------------------------------------------------------------
CREATE TABLE income_entry (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id        BIGINT NOT NULL REFERENCES app_user(id),
    income_id       BIGINT NOT NULL REFERENCES income(id),
    ref_year        SMALLINT NOT NULL,
    ref_month       SMALLINT NOT NULL CHECK (ref_month BETWEEN 1 AND 12),

    planned_amount  NUMERIC(12,2) NOT NULL,
    actual_amount   NUMERIC(12,2),
    received        BOOLEAN NOT NULL DEFAULT false,
    received_date   DATE,

    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (income_id, ref_year, ref_month)
);
CREATE INDEX idx_incomeentry_owner_period ON income_entry(owner_id, ref_year, ref_month);


-- ============================================================
-- AUTENTICAÇÃO / PROVISIONAMENTO (fluxo, não tela de cadastro)
-- ------------------------------------------------------------
-- No login: a API valida o JWT do Firebase, extrai o firebase_uid,
-- e traduz para o app_user.id interno. Se não existir, provisiona:
--
--   INSERT INTO app_user (name, email, firebase_uid)
--   VALUES (:name, :email, :firebase_uid)
--   ON CONFLICT (firebase_uid) DO NOTHING
--   RETURNING id;
--
-- A partir daí, todo o domínio usa app_user.id como owner_id.
-- O firebase_uid nunca vaza para fora da tradução de login.
-- ============================================================


-- ============================================================
-- QUERIES-CHAVE
-- ============================================================

-- ------------------------------------------------------------
-- 1) PROJEÇÃO ANUAL — gera os 12 lançamentos de despesa de um ano
--    a partir dos moldes recorrentes. ON CONFLICT evita duplicar
--    se rodar de novo. Snapshots copiados do molde.
--    :owner = app_user.id ; :year = ano
-- ------------------------------------------------------------
INSERT INTO bill_entry
    (owner_id, bill_id, ref_year, ref_month,
     planned_amount, split_ratio_snapshot, person_id)
SELECT
    b.owner_id, b.id, :year, m.month,
    b.default_amount, b.split_ratio, b.person_id
FROM bill b
CROSS JOIN generate_series(1,12) AS m(month)
WHERE b.owner_id = :owner
  AND b.kind = 'recurring'
  AND b.active = true
ON CONFLICT (bill_id, ref_year, ref_month) DO NOTHING;
-- (equivalente para income_entry, sem person/split)

-- ------------------------------------------------------------
-- 2) RECÁLCULO — reajuste se propaga só pra frente e só nos NÃO PAGOS.
--    Passado pago fica congelado (imutabilidade).
--    Roda depois de atualizar bill.default_amount.
--    :bill_id, :from_year, :from_month, :new_amount
-- ------------------------------------------------------------
UPDATE bill_entry be
SET planned_amount = :new_amount
WHERE be.bill_id = :bill_id
  AND be.paid = false
  AND (be.ref_year > :from_year
       OR (be.ref_year = :from_year AND be.ref_month >= :from_month));

-- ------------------------------------------------------------
-- 3) PAINEL "QUANTO CADA PESSOA ME DEVE" no mês — a receber pendente,
--    agrupado por pessoa. Usa o valor real se já pago, senão o previsto.
--    :owner, :year, :month
-- ------------------------------------------------------------
SELECT
    p.id,
    p.name,
    SUM( COALESCE(be.actual_amount, be.planned_amount)
         * (1 - be.split_ratio_snapshot) )                       AS total_devido,
    SUM( CASE WHEN be.received THEN
         COALESCE(be.actual_amount, be.planned_amount)
         * (1 - be.split_ratio_snapshot) ELSE 0 END )            AS ja_recebido,
    SUM( CASE WHEN NOT be.received THEN
         COALESCE(be.actual_amount, be.planned_amount)
         * (1 - be.split_ratio_snapshot) ELSE 0 END )            AS pendente
FROM bill_entry be
JOIN person p ON p.id = be.person_id
WHERE be.owner_id = :owner
  AND be.ref_year = :year
  AND be.ref_month = :month
  AND be.split_ratio_snapshot < 1
GROUP BY p.id, p.name;
