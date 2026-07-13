# Domínio — Sistema de Orçamento Pessoal

## Propósito
Controle do **fluxo do orçamento mensal** (previsto vs real). Substitui uma planilha. **Fora de escopo:** saldo bancário acumulado, conciliação de extrato, integração bancária.

## Conceito central
Todo item (despesa ou receita) tem um **molde** (cadastro único) que **projeta lançamentos mensais**. Cada lançamento carrega previsto, real e status.
- Futuro = 100% previsto. Presente = edita real e marca pago/recebido. Passado = real congelado.
- Orçamento organizado **por ano** (jan–dez). Virada de ano = projeção do ano seguinte usando os valores atuais dos moldes.

## Entidades
- `app_user` — identidade própria. `firebase_uid` é só vínculo trocável com o provedor. Provisionado no 1º login.
- `category` — categorias de gasto (soft delete).
- `person` — quem deve diferenças de contas compartilhadas. `app_user_id` NULL = rótulo; preenchido = tem acesso (Fase 2).
- `bill` — molde de despesa: `kind` (recurring/one_off), `default_amount`, `split_ratio`, `category_id`, `person_id`.
- `income` — molde de receita: `kind`, `default_amount`.
- `bill_entry` — lançamento mensal de despesa (ver imutabilidade e a-receber abaixo).
- `income_entry` — lançamento mensal de receita.

## split_ratio (fração que é MINHA)
- `1.0` só minha · `0.5` dividida 50/50 · `0.0` passa por mim (débito na minha conta, valor de outra pessoa, ex: telefone).
- Numérico → suporta outras proporções no futuro. `< 1` exige `person_id`.
- Derivados por lançamento: `effective = COALESCE(actual, planned)`; `myShare = effective * split_snapshot`; `receivable = effective * (1 - split_snapshot)`.

## Projeção anual
Gera 12 `*_entry` por molde **recorrente e ativo**, copiando snapshots (`planned_amount ← default_amount`, `split_ratio_snapshot ← split_ratio`, `person_id`). Idempotente (`ON CONFLICT DO NOTHING`); não sobrescreve existentes/pagos. `one_off` não entra na projeção — é adicionado manualmente no mês.

## Imutabilidade e recálculo
- Snapshot no lançamento (não FK ao molde) → editar molde não reescreve passado.
- Pago/recebido congela o lançamento. Só unpay/unreceive descongela.
- Recálculo de reajuste: atualiza `bill.default_amount` e propaga `planned_amount` para lançamentos **não pagos** do mês informado até dezembro. Pagos são pulados.

## Contas compartilhadas / a receber
- O "a receber" **vive dentro do `bill_entry`** (`person_id`, `received`, `received_date`) — sem entidade separada.
- `paid` (eu paguei a conta) e `received` (a pessoa me pagou de volta) são **independentes**.
- Painel do mês agrupa `receivable` por pessoa. Recebimento: individual ou em lote (pagamento único cobrindo várias contas). Valores sempre exatos (sem troco/parcial).

## Dashboards
- Mês: resumo (previsto/real, saldo) + quebra por categoria (usa `myShare`).
- Ano: série de 12 meses + por categoria + totais.

## Históricos (dois eixos distintos)
- **Por pessoa** — a-receber de uma pessoa somando várias contas ao longo do tempo.
- **Por conta** — evolução de um molde (`bill`) mês a mês, com variação.

## Identidade / auth
- Client-managed: front cria conta e loga no Firebase (SDK), envia JWT. Backend valida, traduz `firebase_uid → app_user.id` (provisiona se novo). Domínio usa só o id interno.

## Fase 2 (não implementado)
Vincular login da esposa (`person.app_user_id`) com autorização de dois níveis: dono vê tudo; vinculado vê só o que deve (a fatia a-receber). Schema já suporta.
