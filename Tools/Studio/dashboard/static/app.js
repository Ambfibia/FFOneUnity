"use strict";

const $ = (selector, root = document) => root.querySelector(selector);
const main = $("#main");
const dialog = $("#dialog");
const sections = {overview:"Обзор", bugs:"Баги", plan:"План разработки", inbox:"Входящие", research:"Реверс-инжиниринг"};
const statuses = {new:"В очереди", clarify:"Нужен ответ", ready:"К разработке", active:"В работе", review:"На проверке", done:"Готово", blocked:"Заблокировано"};
const kinds = {note:"Замечание", bug:"Баг", feature:"Фича", task:"Задача"};
const priorities = {high:"Высокий", normal:"Обычный", low:"Низкий"};
const checks = {unassessed:"Не проверено", analyzed:"Есть разбор", tested:"Частичный прогон", confirmed:"Дефект подтверждён", passed:"Проверка пройдена"};
const researchLabels = {partial:"Частично изучено", preserved:"Сохранено", conflict:"Расхождение", unknown:"Не изучено", accepted:"Принято", tested:"Проверено", observed:"Наблюдалось", present:"Найдено", verified:"Проверено", blocked:"Заблокировано", implemented:"Реализовано", planned:"Запланировано"};
let data = {cards:[],research:[]};
let signature = "";
let requestNumber = 0;
let loaded = false;
let page = 1;
const pageSize = 24;
let filters = {};
let toastTimer;

function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[char]));
}
function safeUrl(value, protocols = ["http:", "https:"]) {
  try {
    const url = new URL(value);
    return protocols.includes(url.protocol) ? url.href : "";
  } catch { return ""; }
}
function badge(value, labels) {
  const style = Object.hasOwn(labels,value) ? value : "";
  return `<span class="status ${esc(style)}">${esc(labels[value] || value || "—")}</span>`;
}
function options(values, selected, prefix = "") {
  const entries = Object.entries(values);
  if (prefix) entries.unshift(["",prefix]);
  return entries.map(([key,label]) => `<option value="${esc(key)}"${key === selected ? " selected" : ""}>${esc(label)}</option>`).join("");
}
function route() {
  const [name,query] = location.hash.slice(1).split("?");
  return {tab:Object.hasOwn(sections,name) ? name : "overview", params:new URLSearchParams(query)};
}
function routeUrl(tab, values = {}) {
  const q = new URLSearchParams(Object.entries(values).filter(([,value]) => value != null && value !== ""));
  return "#" + tab + (q.size ? "?" + q : "");
}
function navigate(tab, values) {
  const next = routeUrl(tab,values);
  if (location.hash === next) loadRoute(); else location.hash = next;
}
function showToast(message) {
  clearTimeout(toastTimer);
  $("#toast").textContent = message;
  toastTimer = setTimeout(() => $("#toast").textContent = "", 4200);
}
async function api(url, payload) {
  const init = {cache:"no-store"};
  if (payload !== undefined) Object.assign(init,{method:"POST", headers:{"Content-Type":"application/json","X-Studio":"1"}, body:JSON.stringify(payload)});
  const res = await fetch(url, init);
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(body.error || body.message || ("Ответ сервера " + res.status));
  return body;
}
function reportError(err) {
  const box = $("#error");
  box.hidden = false;
  box.innerHTML = `${esc(err.message || err)} <button type="button" data-retry>Повторить</button>`;
}
async function refresh(force = false) {
  const serial = ++requestNumber;
  try {
    const next = await api("/api/state");
    if (serial !== requestNumber) return;
    if (!Array.isArray(next.cards) || !Array.isArray(next.research)) throw new Error("Не удалось прочитать реестр проекта.");
    const fingerprint = JSON.stringify([next.cards,next.research]);
    const changed = fingerprint !== signature;
    data = next;
    signature = fingerprint;
    loaded = true;
    $("#error").hidden = true;
    $("#connection").textContent = "Обновлено " + new Date().toLocaleTimeString("ru",{hour:"2-digit",minute:"2-digit"});
    if ((changed || force) && !dialog.open) {
      const input = document.activeElement;
      const typing = main.contains(input) && ["INPUT","TEXTAREA","SELECT"].includes(input.tagName);
      if (typing && !force) {
        if ($("#results")) renderResults();
      } else render();
    }
  } catch (err) {
    if (serial !== requestNumber) return;
    $("#connection").textContent = "Нет связи";
    reportError(err);
  }
}
function compose() {
  return `<form class="compose" id="quick-note"><input name="remark" maxlength="8000" aria-label="Записать замечание" placeholder="Записать замечание…" autocomplete="off"><button type="submit" class="secondary">Добавить</button><button type="button" class="primary" data-new="feature">＋ Новая фича</button></form>`;
}
function meter(value, total) {
  const percent = total ? Math.round(value * 10000 / total) / 100 : 0;
  return `<span class="track" aria-hidden="true"><i style="width:${percent}%"></i></span>`;
}
function metric(title, value, total, caption, tab, query) {
  return `<button class="metric" data-go="${esc(routeUrl(tab,query))}"><span class="metric-name">${title}</span><span class="metric-number">${value} <small>/ ${total}</small></span>${meter(value,total)}<span class="metric-caption">${caption} ↗</span></button>`;
}
function sortedCards(cards) {
  const rank = {high:0,normal:1,low:2};
  return cards.slice().sort((a,b) => (rank[a.priority] ?? 1) - (rank[b.priority] ?? 1));
}
function legacyState(card) {
  if (card.sourceSection === "Открыто") return "Открыто · " + (card.historical || "без отметки");
  return card.historical || "—";
}
function table(cards, bugs) {
  if (!cards.length) return '<p class="empty">По этим условиям карточек нет.</p>';
  return `<div class="table-scroll"><table><thead><tr><th scope="col">${bugs ? "Сценарий" : "Задача"}</th><th scope="col">${bugs ? "FFOneClient" : "Раздел"}</th><th scope="col">${bugs ? "Проверка Unity" : "Состояние"}</th></tr></thead><tbody>${cards.map(c => `<tr><td><button class="row-link" data-card="${esc(c.id)}">${esc(c.title)}</button></td><td>${esc(bugs ? legacyState(c) : c.area || "—")}</td><td>${bugs ? badge(c.verification || "unassessed",checks) : badge(c.status,statuses)}</td></tr>`).join("")}</tbody></table></div>`;
}
function areaChart(cards) {
  const counts = new Map();
  for (const c of cards) counts.set(c.area || "", (counts.get(c.area || "") || 0) + 1);
  const rows = [...counts].sort((a,b) => b[1]-a[1]);
  if (!rows.length) return '<p class="empty">Очередь пуста.</p>';
  return rows.slice(0,6).map(([area,n]) => `<button class="area-row" data-go="${esc(routeUrl("bugs",{area:area || "__empty",open:"1"}))}"><span>${esc(area || "Без раздела")}</span>${meter(n,rows[0][1])}<b>${n}</b></button>`).join("");
}
function distribution(entries,total,linkFor) {
  return entries.map(([key,label,count]) => `<button class="area-row" data-go="${esc(linkFor(key))}"><span>${esc(label)}</span>${meter(count,total)}<b>${count}</b></button>`).join("");
}
function overview() {
  const bugs = data.cards.filter(c => c.kind === "bug");
  const features = data.cards.filter(c => c.kind === "feature");
  const focus = sortedCards(data.cards.filter(c => ["active","clarify","blocked","ready"].includes(c.status))).slice(0,4);
  const reviewed = bugs.filter(c => c.verification && c.verification !== "unassessed");
  const terminal = bugs.filter(c => ["confirmed","passed"].includes(c.verification));
  const sample = [...reviewed, ...bugs.filter(c => !c.verification || c.verification === "unassessed")].slice(0,4);
  const upcoming = focus.length ? focus : sortedCards(features.filter(c => c.status !== "done")).slice(0,4);
  const stages = [...new Set(data.research.map(r => r.status || "unknown"))];
  const researchCounts = stages.map(s => [s,researchLabels[s] || s,data.research.filter(r => (r.status || "unknown") === s).length]);
  const featureCounts = Object.entries(statuses).map(([key,label]) => [key,label,features.filter(c => c.status === key).length]);
  main.innerHTML = compose() + `<section aria-label="Прогресс проекта" class="metrics">
    ${metric("Разбор багов",reviewed.length,bugs.length,"есть результат разбора","bugs",{check:"assessed"})}
    ${metric("Итог перепроверки",terminal.length,bugs.length,"есть итог по сценарию","bugs",{check:"terminal"})}
    ${metric("Новые фичи",features.filter(c => c.status === "done").length,features.length,"завершено из запланированных","plan",{kind:"feature"})}
  </section><section><div class="section-line"><h2>Перепроверка в Unity</h2><a class="text-link" href="#bugs">Все баги · ${bugs.length} →</a></div>${table(sample,true)}</section>
  <div class="lower-grid"><section><div class="section-line"><h2>План разработки</h2><a class="text-link" href="#plan">Весь план →</a></div><div class="queue">
  ${upcoming.map(c => `<button class="plain-row" data-card="${esc(c.id)}"><span><em>${esc(c.area || kinds[c.kind])}</em><strong>${esc(c.title)}</strong></span>${badge(c.status,statuses)}</button>`).join("") || '<p class="empty">План пока пуст.</p>'}
  </div></section><section><div class="section-line"><h2>Баги по разделам</h2></div>${areaChart(bugs.filter(c => c.status !== "done"))}</section></div>
  <div class="lower-grid"><section><div class="section-line"><h2>Фичи по состоянию</h2></div>${distribution(featureCounts,features.length,s => routeUrl("plan",{kind:"feature",status:s}))}</section>
  <section><div class="section-line"><h2>Реверс-инжиниринг</h2><a class="text-link" href="#research">Все →</a></div>${distribution(researchCounts,data.research.length,s => routeUrl("research",{stage:s})) || '<p class="empty">Исследований пока нет.</p>'}</section></div>`;
}
function scopeCards(tab) {
  return data.cards.filter(c => tab === "bugs" ? c.kind === "bug" : tab === "inbox" ? c.kind === "note" || c.status === "clarify" : true);
}
function matchCheck(card, key) {
  const check = card.verification || "unassessed";
  if (key === "assessed") return check !== "unassessed";
  if (key === "terminal") return ["passed","confirmed"].includes(check);
  return !key || check === key;
}
function listing(tab) {
  const cards = scopeCards(tab);
  const areaNames = [...new Set(cards.map(c => c.area).filter(Boolean))].sort((a,b) => a.localeCompare(b,"ru"));
  const areas = Object.fromEntries([["__empty","Без раздела"],...areaNames.map(area => [area,area])]);
  const allChecks = {assessed:"Есть разбор", terminal:"Есть итог", ...checks};
  main.innerHTML = `<div class="list-head section-line"><h2>${sections[tab]}</h2><button type="button" class="primary" data-new="${tab === "plan" ? "feature" : tab === "bugs" ? "bug" : "note"}">${tab === "plan" ? "＋ Новая фича" : tab === "bugs" ? "＋ Новый баг" : "＋ Замечание"}</button></div>
  <div class="filters"><input id="query" type="search" value="${esc(filters.q)}" placeholder="Поиск по карточкам…" aria-label="Поиск по карточкам">
  <select data-filter="area" aria-label="Раздел">${options(areas,filters.area,"Все разделы")}</select>
  ${tab === "plan" ? `<select data-filter="kind" aria-label="Тип">${options(kinds,filters.kind,"Все типы")}</select>` : ""}
  ${tab === "bugs" ? `<select data-filter="check" aria-label="Результат перепроверки">${options(allChecks,filters.check,"Все проверки")}</select>` : ""}
  ${tab === "bugs" ? `<select data-filter="origin" aria-label="Источник FFOneClient">${options({closed:"Закрытые на двух машинах",other:"Остальные записи FFOneClient",local:"Добавлены в Studio"},filters.origin,"Любая история FFOneClient")}</select>` : ""}
  <select data-filter="priority" aria-label="Приоритет">${options(priorities,filters.priority,"Любой приоритет")}</select></div>
  <div class="stats" aria-label="Фильтр по состоянию"></div><div id="results"></div>`;
  renderResults();
}
function renderResults() {
  const tab = route().tab;
  const search = (filters.q || "").trim().toLocaleLowerCase("ru");
  const cards = scopeCards(tab).filter(c =>
    (!filters.area || (c.area || "__empty") === filters.area) && (!filters.priority || c.priority === filters.priority) &&
    (!filters.kind || c.kind === filters.kind) && matchCheck(c,filters.check) && (!filters.open || c.status !== "done") &&
    (!filters.origin || filters.origin === (!c.historical ? "local" : c.historical.startsWith("Закрыто") ? "closed" : "other")) &&
    (!search || [c.title,c.id,c.area,c.notes,c.evidence].some(v => String(v || "").toLocaleLowerCase("ru").includes(search))));
  const chip = (key,label,n) => `<button class="chip${(filters.status || "") === key ? " selected" : ""}" aria-pressed="${(filters.status || "") === key}" data-status="${key}">${label}<b>${n}</b></button>`;
  $(".stats",main).innerHTML = chip("","Все",cards.length) + Object.entries(statuses).map(([k,v]) => chip(k,v,cards.filter(c=>c.status === k).length)).join("");
  const result = sortedCards(cards.filter(c => !filters.status || c.status === filters.status));
  const pages = Math.max(1,Math.ceil(result.length/pageSize));
  page = Math.min(page,pages);
  const first = (page-1)*pageSize;
  const reset = Object.values(filters).some(Boolean) ? '<button class="text-link" data-reset>Сбросить фильтры</button>' : "";
  $("#results",main).innerHTML = `<div class="section-line"><span class="summary">Карточек: ${result.length}${filters.open ? " · В очереди и работе" : ""}</span>${reset}</div>` +
    table(result.slice(first,first+pageSize),tab === "bugs") + (pages > 1 ?
    `<div class="paging"><button class="secondary" data-page="-1" ${page === 1 ? "disabled" : ""}>← Назад</button><span>${page} / ${pages}</span><button class="secondary" data-page="1" ${page === pages ? "disabled" : ""}>Далее →</button></div>` : "");
}
function sourceRef(value) {
  if (!value) return "";
  const url = value.startsWith("/source?") ? value : safeUrl(value);
  return url ? `<a href="${esc(url)}" target="_blank" rel="noopener noreferrer">Открыть источник ↗</a>` : `<span class="wrap-text">${esc(value)}</span>`;
}
function research() {
  const rows = data.research;
  const groups = [...new Set(rows.map(x => x.status || "unknown"))];
  const selected = filters.stage;
  main.innerHTML = `<div class="list-head section-line"><h2>Реверс-инжиниринг</h2><span class="summary">Записей в реестре: ${rows.length}</span></div>
    <div class="stats"><a class="chip${!selected?" selected":""}" href="#research">Все<b>${rows.length}</b></a>${groups.map(s => `<a class="chip${selected===s?" selected":""}" href="${esc(routeUrl("research",{stage:s}))}">${esc(researchLabels[s] || s)}<b>${rows.filter(r => (r.status || "unknown") === s).length}</b></a>`).join("")}</div>
    <div class="table-scroll"><table><thead><tr><th>Объект исследования</th><th>Состояние</th><th>Источник</th></tr></thead><tbody>${rows.filter(r => !selected || (r.status||"unknown") === selected).map(r => `<tr><td>${esc(r.title)}</td><td>${badge(r.status || "unknown",researchLabels)}</td><td>${sourceRef(r.source)}</td></tr>`).join("")}</tbody></table></div>${rows.length ? "" : '<p class="empty">В реестре пока нет исследований.</p>'}`;
}

const cardFields = ["title","kind","area","priority","status","verification","notes","evidence"];
let editingId = null;
let originalFields = null;
let busy = false;
function historyDetails(card) {
  const entries = (card.history || []).slice(-8).reverse();
  if (!entries.length) return "";
  const labels = {notes:"Заметки", evidence:"Результат", title:"Формулировка", status:"Состояние", priority:"Приоритет", verification:"Перепроверка",kind:"Тип",area:"Раздел",chat:"Чат",triageChat:"Чат разбора"};
  const dictionaries = {kind:kinds,status:statuses,priority:priorities,verification:checks};
  return `<details class="history"><summary>История обновлений · ${card.history.length}</summary>${entries.map(e => `<div><time>${esc(new Date(e.time).toLocaleString("ru"))}</time><ul>${Object.entries(e.changes || {}).map(([k,v])=>`<li>${esc(labels[k] || k)} · ${esc(dictionaries[k]?.[v] || v)}</li>`).join("")}</ul></div>`).join("")}</details>`;
}
function field(label,control) { return `<label class="field"><span>${label}</span>${control}</label>`; }
function selectField(label,name,values,selected) {
  return field(label,`<select name="${name}">${options(values,selected)}</select>`);
}
function presentCard(c, kind = "note", title = "") {
  if (busy) return;
  editingId = c ? c.id : null;
  const record = {title,kind,area:"",status:"new",priority:"normal",verification:"unassessed",evidence:"",notes:"",...c};
  originalFields = Object.fromEntries(cardFields.map(k => [k,String(record[k] || "")]));
  const formTitle = c ? "Карточка задачи" : {feature:"Новая фича",bug:"Новый баг",note:"Новое замечание",task:"Новая задача"}[kind];
  $("#dialog-content").innerHTML = `<div class="dialog-head"><div>
    ${c ? `<span class="eyeline">${esc(c.id)}</span>` : ""}
    <h2 id="dialog-title">${formTitle}</h2></div><button type="button" class="close" aria-label="Закрыть" data-close>×</button></div>
    <form id="card-form"><div class="fields">
    ${field("Формулировка",`<textarea name="title" rows="2" required maxlength="8000" placeholder="Что нужно сделать?">${esc(record.title)}</textarea>`)}
    <div class="paired">${selectField("Тип","kind",kinds,record.kind)}${field("Раздел",`<input name="area" list="area-hints" value="${esc(record.area)}" maxlength="200" placeholder="Например, интерфейс">`)}</div>
    <datalist id="area-hints">${[...new Set(data.cards.map(x => x.area).filter(Boolean))].map(a => `<option value="${esc(a)}"></option>`).join("")}</datalist>
    ${c ? `<div class="paired">${selectField("Состояние","status",statuses,record.status)}${selectField("Приоритет","priority",priorities,record.priority)}</div>
    <div class="field" id="verification-field"${record.kind !== "bug" ? " hidden" : ""}>${selectField("Перепроверка FFOneUnity","verification",checks,record.verification || "unassessed")}</div>` : ""}
    ${field("Заметки и уточнения",`<textarea name="notes" rows="3" placeholder="Условия, ожидаемый результат, ответ на вопрос…">${esc(record.notes)}</textarea>`)}
    ${c ? field("Результат и свидетельства",`<textarea name="evidence" rows="2" placeholder="Что проверено, где найти результат…">${esc(record.evidence)}</textarea>`) : ""}
    ${c && (c.historical || c.source) ? `<p class="meta">${c.historical ? `FFOneClient · ${esc(legacyState(c))}` : ""} ${sourceRef(c.source)}</p>` : ""}
    ${c && c.original && c.original !== c.title ? `<details class="history"><summary>Исходное замечание</summary><p>${esc(c.original)}</p></details>` : ""}
    ${c ? historyDetails(c) : ""}
    </div><p id="form-error" class="notice" role="alert" hidden></p><p class="meta" id="form-progress" role="status"></p><div class="actions">
    ${c ? `<button class="text-link" type="button" data-triage>${c.triageChat ? "Чат разбора" : "Разобрать замечание"} ↗</button><button class="secondary" type="button" data-chat>${c.chat ? "Открыть чат" : "Начать чат"} ↗</button>` : kind === "note" ? '<button class="secondary" type="button" data-triage>Создать и разобрать ↗</button>' : '<button class="secondary" type="button" data-chat>Создать и начать чат ↗</button>'}
    <button class="primary" type="submit">${c ? "Сохранить" : "Создать карточку"}</button></div></form>`;
  if (!dialog.open) dialog.showModal();
  if (!c) $('textarea[name="title"]',dialog).focus();
}
function changedDraft() {
  const current = Object.fromEntries(new FormData($("#card-form")).entries());
  for (const k of Object.keys(current)) if (typeof current[k] === "string") current[k] = current[k].trim();
  if (!current.title) throw new Error("Введите формулировку задачи.");
  if (!current.area) delete current.area;
  if (!editingId) return current;
  return Object.fromEntries(Object.entries(current).filter(([k,v]) => v !== (originalFields[k] || "").trim()));
}
async function actOnCard(action) {
  if (busy || !$("#card-form").reportValidity()) return;
  const errBox = $("#form-error");
  errBox.hidden = true;
  busy = true;
  $("#form-progress").textContent = action === "save" ? "Сохраняем карточку…" : "Запускаем чат Codex…";
  const buttons = [...dialog.querySelectorAll("button")];
  buttons.forEach(b => b.disabled = true);
  try {
    const draft = changedDraft();
    const cardPath = editingId ? "/api/cards/" + encodeURIComponent(editingId) : "/api/cards";
    if (Object.keys(draft).length) {
      const saved = await api(cardPath,draft);
      if (!editingId && action !== "save") {
        editingId = saved.id;
        cardLocation(editingId);
        $("#dialog-title").textContent = "Карточка задачи";
        $("button[type=submit]",dialog).textContent = "Сохранить";
      }
      Object.assign(originalFields,draft);
    }
    if (action !== "save") {
      const result = await api("/api/chat/" + encodeURIComponent(editingId),{triage:action === "triage"});
      const link = safeUrl(result.url,["https:","http:","codex:"]);
      if (result.url && !link) throw new Error("Сервер вернул неподдерживаемый адрес чата.");
      if (link) {
        // An explicit anchor also works when the browser blocks asynchronous popups.
        const a = document.createElement("a");
        a.href = link;
        a.textContent = "Перейти в чат ↗";
        a.target = "_blank";
        a.rel = "noopener";
        const msg = document.createElement("p");
        msg.className = "notice";
        msg.append("Чат готов. ",a);
        $("#dialog-content .launch-result")?.remove();
        msg.classList.add("launch-result");
        $("#dialog-content").append(msg);
        a.click();
        await refresh();
        return;
      }
      showToast("Чат открыт локально в Codex.");
    } else showToast(editingId ? "Карточка сохранена." : "Карточка создана.");
    dialog.close();
    await refresh(true);
  } catch (err) {
    errBox.textContent = err.message || "Не удалось выполнить операцию.";
    errBox.hidden = false;
  } finally {
    busy = false;
    if ($("#form-progress")) $("#form-progress").textContent = "";
    buttons.forEach(b => b.disabled = false);
  }
}
function render() {
  if (!loaded) return;
  const {tab} = route();
  document.title = sections[tab] + " · FusionFall Studio";
  for (const a of document.querySelectorAll("nav a[href^='#']")) {
    if (a.hash === "#" + tab) a.setAttribute("aria-current","page"); else a.removeAttribute("aria-current");
  }
  if (tab === "overview") overview();
  else if (tab === "research") research();
  else listing(tab);
  const cardId = route().params.get("card");
  if (cardId && !dialog.open && !busy) {
    const card = data.cards.find(item => item.id === cardId);
    if (card) presentCard(card);
    else reportError(new Error("Карточка с таким ID не найдена."));
  }
}
function cardLocation(id) {
  const {tab,params} = route();
  if (id) params.set("card",id); else params.delete("card");
  history.replaceState(null,"",routeUrl(tab,Object.fromEntries(params)));
}
function loadRoute() {
  const {params} = route();
  filters = Object.fromEntries(params);
  delete filters.card;
  page = 1;
  if (dialog.open && !busy) dialog.close();
  render();
}
function setFilter(key,value) {
  filters[key] = value;
  page = 1;
  history.replaceState(null,"",routeUrl(route().tab,filters));
  renderResults();
}
main.addEventListener("click",e => {
  const b = e.target.closest("button");
  if (!b) return;
  if (b.dataset.go) location.hash = b.dataset.go;
  if (b.dataset.new) presentCard(null,b.dataset.new);
  if (b.dataset.card) {
    const c = data.cards.find(x => x.id === b.dataset.card);
    if (c) { cardLocation(c.id); presentCard(c); }
  }
  if (b.hasAttribute("data-status")) setFilter("status",b.dataset.status);
  if (b.hasAttribute("data-reset")) navigate(route().tab);
  if (b.dataset.page) { page += Number(b.dataset.page); renderResults(); }
});
main.addEventListener("input",e => { if (e.target.id === "query") setFilter("q",e.target.value); });
main.addEventListener("change",e => { if (e.target.dataset.filter) setFilter(e.target.dataset.filter,e.target.value); });
main.addEventListener("submit",e => {
  if (e.target.id !== "quick-note") return;
  e.preventDefault();
  presentCard(null,"note",new FormData(e.target).get("remark").trim());
});
dialog.addEventListener("click",e => {
  if (e.target.closest("[data-close]") && !busy) dialog.close();
  if (e.target.closest("[data-chat]")) actOnCard("chat");
  if (e.target.closest("[data-triage]")) actOnCard("triage");
});
dialog.addEventListener("submit",e => { e.preventDefault(); actOnCard("save"); });
dialog.addEventListener("change",e => {
  if (e.target.name === "kind" && $("#verification-field")) $("#verification-field").hidden = e.target.value !== "bug";
});
dialog.addEventListener("cancel",e => { if (busy) e.preventDefault(); });
dialog.addEventListener("close",() => {
  if (!dialog.open && route().params.get("card") === editingId) cardLocation(null);
  render();
});
$("#refresh").addEventListener("click",() => refresh(true));
$(".architecture").addEventListener("click",event => {
  if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
  const architecture = window.open("/architecture","ffone-architecture","popup,width=1440,height=900,resizable=yes,scrollbars=yes");
  if (architecture) { event.preventDefault(); architecture.focus(); }
});
$("#error").addEventListener("click",e => { if (e.target.closest("[data-retry]")) refresh(true); });
window.addEventListener("hashchange",loadRoute);
window.addEventListener("focus",() => refresh());
setInterval(() => { if (!document.hidden) refresh(); },12000);
loadRoute();
refresh();
