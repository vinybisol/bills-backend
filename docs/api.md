# API — Endpoints

Todos os endpoints vivem sob o prefixo **`/api/v1`**. Todos exigem `Authorization: Bearer <firebase-jwt>`. `owner_id` resolvido do token. Derivados (`effective`, `myShare`, `receivable`) calculados na resposta, não persistidos.

## Infra
- `GET /api/v1/health` → resolve/provisiona o app_user e confirma liveness autenticada.

## Identidade
- `GET /api/v1/me` → perfil do app_user (id, name, email). Provisiona se novo.

## Cadastros (soft delete)
- `/api/v1/categories` — GET, POST, PUT `/{id}`, DELETE `/{id}` (desativa)
- `/api/v1/persons` — idem
- `/api/v1/incomes` — idem (molde: kind, default_amount). Contrato padronizado (Result → HTTP):
  - `POST` {name, kind: `recurring`|`one_off`, defaultAmount ≥ 0} → 201 + `Location: /api/v1/incomes/{id}` + {id,name,kind,defaultAmount}.
  - `GET` → 200 com a lista (ordenada por nome, só ativos) ou **204 se vazia**.
  - `PUT /{id}` {name,kind,defaultAmount} → 200 com o DTO atualizado; 404 (ProblemDetails) se não existe/inativo/de outro owner.
  - `DELETE /{id}` → 204 (soft delete, `active=false`); 404 (ProblemDetails) se não existe/já inativo/de outro owner.
  - Validação → 400 `application/problem+json` (ValidationProblem) com `errors` por campo: `name` (vazio), `kind` (valor fora do enum), `defaultAmount` (negativo). `kind` string desconhecido → 400 no binding.
- `/api/v1/bills` — molde: categoryId, kind, defaultAmount, splitRatio, personId. Contrato padronizado (Result → HTTP):
  - `POST` {name, categoryId, kind: `recurring`|`one_off`, defaultAmount ≥ 0, splitRatio ∈ [0,1], personId?} → 201 + `Location: /api/v1/bills/{id}` + {id,name,categoryId,kind,defaultAmount,splitRatio,personId}.
  - `GET` → 200 com a lista (ordenada por nome, só ativos) ou **204 se vazia**.
  - `PUT /{id}` (mesmo corpo) → 200 com o DTO atualizado; 404 (ProblemDetails) se o molde não existe/inativo/de outro owner.
  - `DELETE /{id}` → 204 (soft delete, `active=false`); 404 (ProblemDetails) se não existe/já inativo/de outro owner.
  - Validação → 400 ValidationProblem com `errors` por campo: `name` (vazio), `kind` (fora do enum), `defaultAmount` (negativo), `splitRatio` (fora de [0,1]), `personId` (split<1 exige; =1 proíbe). Validação roda antes de qualquer acesso ao banco.
  - `categoryId`/`personId` inexistente, inativo ou de outro owner → 404 (ProblemDetails) em POST/PUT.

## Projeção
- `POST /api/v1/projection/{year}` → gera 12 entries por molde **recorrente ativo** (bills e incomes; `one_off` e moldes desativados são ignorados). Cada entry leva **snapshot** de `plannedAmount` (e, em bills, `splitRatioSnapshot`/`personId`) — nunca referência ao molde. Idempotente: meses que já têm entry (por molde + ano + mês) são pulados; entries existentes (inclusive pagas) nunca são alteradas. Contrato padronizado (Result → HTTP):
  - 200 {year, billEntriesCreated, incomeEntriesCreated, skipped} (também quando nada é criado).
  - 400 ValidationProblem com `errors.year` se `year` fora de **2000–2100**; nada é persistido. `year` não numérico → 404 (constraint de rota `{year:int}`).
  - 409 ProblemDetails se uma projeção/lançamento concorrente inserir o mesmo mês entre a leitura e a gravação (nada é persistido; repetir a chamada é seguro).
  - 401 sem token válido.

## Lançamentos
Contrato padronizado (Result → HTTP). Todas as rotas: 401 sem token válido; lançamentos inexistentes ou de outro owner → 404 ProblemDetails; validação → 400 ValidationProblem com `errors` por campo (roda antes de qualquer acesso ao banco). Lançamento **pago** (bill) / **recebido** (income) é congelado → 409 ProblemDetails (`BillEntry.Frozen` / `IncomeEntry.Frozen`); só `unpay`/`unreceive` descongela. `paid` (eu paguei) e `received` (a pessoa me pagou de volta) são independentes: `pay`/`unpay` nunca alteram `received`.
- `GET /api/v1/entries?year=&month=` → **200 sempre** (mesmo sem lançamentos: listas vazias e totais zerados — a resposta é um objeto, não uma lista, por isso não há 204) com {year, month, bills[], incomes[], totals}. Bills ordenados por categoria e nome; nomes de molde/categoria/pessoa resolvidos mesmo se desativados; `person` só quando o snapshot tem `personId`. 400 com `errors.year` (ausente ou fora de **2000–2100**) e/ou `errors.month` (ausente ou fora de 1–12). `totals.receivable` = a receber **pendente** (bill entries com `received=false`, alias de `receivablePending`); `totals.received` = já recebido no mês (alias de `receivableReceived`). `received + receivable` = total a receber do mês. `totals.paidFull` = valor cheio (não myShare) dos bill entries pagos. Três saldos: `saldoPrevistoOtimista` (= `saldoPrevisto`, assume que todo pendente será recebido) = incomesPlanned − mySharePlanned; `saldoPrevistoPiorCaso` = saldoPrevistoOtimista − receivablePending (assume que o pendente nunca será pago); `saldoRealizado` (= `saldoReal`) = (incomesReceived + receivableReceived) − paidFull. Nota: `saldoReal` mudou de semântica — antes era baseado no myShare dos bills pagos, agora usa o valor cheio pago (`paidFull`).
- `POST /api/v1/entries/bill` {billId, year, month, plannedAmount?} → 201 + `Location: /api/v1/entries/bill/{id}` + {id, billId, refYear, refMonth, plannedAmount, actualAmount, splitRatioSnapshot, personId, paid, paidDate, received, receivedDate}. Só moldes `one_off`; o lançamento leva **snapshot** de `plannedAmount` (default do molde se omitido), `splitRatioSnapshot` e `personId`. 400: `year` (2000–2100), `month` (1–12), `plannedAmount` (negativo), `billId` (molde recorrente). 404 se o molde não existe/inativo/de outro owner. 409 se já existe lançamento do molde no mês.
- `POST /api/v1/entries/income` {incomeId, year, month, plannedAmount?} → 201 + `Location: /api/v1/entries/income/{id}` + {id, incomeId, refYear, refMonth, plannedAmount, actualAmount, received, receivedDate}. Mesmas regras (400 com `incomeId` para molde recorrente; 404; 409 duplicado).
- `DELETE /api/v1/entries/bill/{id}` → 204 se não pago; 409 se pago. `DELETE /api/v1/entries/income/{id}` → 204 se não recebido; 409 se recebido.
- `PATCH /api/v1/entries/bill/{id}` {plannedAmount?, actualAmount?} → 200 com o DTO; 409 se congelado; 400 `plannedAmount`/`actualAmount` negativos. `PATCH /api/v1/entries/income/{id}` idem.
- `POST /api/v1/entries/bill/{id}/pay` {actualAmount?, paidDate?} (corpo opcional) → 200 com o DTO; congela. `actualAmount` omitido = `plannedAmount`; `paidDate` omitido = agora (UTC), informado = meia-noite UTC do dia. 400 `actualAmount` negativo; 409 se já pago (valores congelados não são sobrescritos). `/unpay` → 200 com o DTO; descongela (limpa `paid`/`paidDate`, mantém `actualAmount` e `received`); idempotente.
- `POST /api/v1/entries/income/{id}/receive` {actualAmount?, receivedDate?} (corpo opcional) → 200; mesmas regras de `pay` (409 se já recebido). `/unreceive` → 200; descongela; idempotente.

## Recálculo
- `POST /api/v1/bills/{billId}/recalculate` {fromYear,fromMonth,newAmount} → atualiza default_amount + planned dos não-pagos ≥ mês (pagos ficam congelados). 200 {billId, updatedEntries, skippedPaid, newDefaultAmount}. 400 ValidationProblem (`fromMonth` fora de 1–12, `newAmount` negativo); 404 ProblemDetails se o molde não existe/inativo/de outro owner.

## Dashboards
Contrato padronizado (Result → HTTP). Todas as rotas: 401 sem token válido; validação → 400 ValidationProblem com `errors` por campo (roda antes de qualquer acesso ao banco); respostas são objetos → **200 sempre** (sem dados: estrutura zerada, nunca 204). Só dados do owner autenticado; lançamentos cujo molde/categoria foi desativado continuam contando, e o nome da categoria é resolvido mesmo assim.
- `GET /api/v1/dashboard/month?year=&month=` → {year, month, summary, byCategory[{categoryId, category, plannedMyShare, actualMyShare, diff}]}. `byCategory` agrupa por `categoryId`, ordenado por `plannedMyShare` desc; categorias sem lançamentos no mês são omitidas. 400 `errors.year` (ausente ou fora de 2000–2100) e/ou `errors.month` (ausente ou fora de 1–12). `summary` = {plannedExpense, actualExpense, plannedIncome, actualIncome, saldoPrevisto, saldoReal, billsPaid, billsTotal, incomesReceived, incomesTotal, receivablePending, receivableReceived, paidFull, saldoPrevistoOtimista, saldoPrevistoPiorCaso, saldoRealizado}; despesas são myShare (planejado sobre todos; real só dos pagos). `summary` inclui `receivablePending`, `receivableReceived`, `paidFull` (valor cheio dos bill entries pagos) e os três saldos `saldoPrevistoOtimista`/`saldoPrevistoPiorCaso`/`saldoRealizado` — mesma semântica de `GET /api/v1/entries` (ver acima), incluindo a mudança de `saldoReal` para usar `paidFull` em vez do myShare dos bills pagos.
- `GET /api/v1/dashboard/year?year=` → {year, months[12]{month, plannedExpense, actualExpense, plannedIncome, actualIncome, saldoPrevisto, saldoReal}, byCategory[{categoryId, category, plannedMyShare, actualMyShare}], totals}. `months` sempre tem 12 itens (1–12, zerados quando sem dados); aqui `saldoReal` = actualIncome − actualExpense (myShare dos pagos). `totals` = soma dos 12 meses. 400 `errors.year` (ausente ou fora de 2000–2100).

## A receber
Contrato padronizado (Result → HTTP). "A receber" vive dentro do `bill_entry` (`personId`, `received`, `receivedDate`); só lançamentos com `splitRatioSnapshot < 1` (e `personId`) são recebíveis. `received` (a pessoa me pagou de volta) é independente de `paid`: nenhuma rota abaixo altera `paid`/`paidDate`, e lançamento pago **não** bloqueia marcar/desmarcar. Todas as rotas: 401 sem token válido; lançamento/pessoa inexistente ou de outro owner → 404 ProblemDetails; validação → 400 ValidationProblem com `errors` por campo.
- `GET /api/v1/receivables/month?year=&month=` → **200 sempre** (objeto; sem recebíveis: `byPerson: []`, `totalPendenteGeral: 0`) com {year, month, byPerson[{personId, name, totalDevido, jaRecebido, pendente, items[{entryId, bill, receivable, received}]}], totalPendenteGeral}. Pessoas ordenadas por nome, itens por id; nomes de molde/pessoa resolvidos mesmo se desativados. 400 `year` (ausente ou fora de 2000–2100) / `month` (ausente ou fora de 1–12).
- `POST /api/v1/receivables/{entryId}/mark` {receivedDate?} (corpo opcional) → 200 com o DTO do bill entry {id, billId, refYear, refMonth, plannedAmount, actualAmount, splitRatioSnapshot, personId, paid, paidDate, received, receivedDate}. `receivedDate` omitido = agora (UTC), informado = meia-noite UTC do dia. Idempotente (remarcar reaplica a data). 400 `errors.entryId` se o lançamento não é recebível (split = 1).
- `POST /api/v1/receivables/{entryId}/unmark` → 200 com o DTO; limpa `received`/`receivedDate`; idempotente.
- `POST /api/v1/receivables/mark-batch` {entryIds, receivedDate?} → 200 {marked} (quantidade de ids **distintos**). **Tudo-ou-nada** (uma única transação): se algum id não existir/for de outro owner → 404 ProblemDetails (lista os ids); se algum não for recebível → 400 `errors.entryIds`; em ambos os casos nada é marcado. 400 `errors.entryIds` se a lista vier ausente ou vazia.
- `GET /api/v1/receivables/history?personId=&fromYear=&fromMonth=&toYear=&toMonth=&status=` → **200 sempre** {personId, name, totals{totalDevido, totalRecebido, totalPendente}, items[{entryId, bill, year, month, receivable, received, receivedDate}]}; itens do mais recente ao mais antigo; totais sobre o recorte filtrado. Período: um limite só vale com ano **e** mês. `status` = `received` | `pending`; qualquer outro valor (ou ausente) = todos. 400 `errors.personId` se ausente; 404 se a pessoa não existe, está desativada ou é de outro owner.

## Históricos
- `GET /api/v1/bills/{billId}/history?fromYear=&fromMonth=&toYear=&toMonth=` → 200 header do molde {billId,name,category,splitRatio,person} + summary(avgEffective/minEffective/maxEffective/totalPaidMyShare) + items (com variation vs anterior). Resolve também moldes desativados. 404 ProblemDetails se não existe/de outro owner.

## Códigos comuns
- 401 sem/invalid token · 404 recurso de outro owner · 400 validação · 409 imutabilidade/duplicado.
