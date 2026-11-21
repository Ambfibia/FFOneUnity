"use strict";
const content = document.querySelector("#content");
const notice = document.querySelector("#notice");
const tabs = {overview:"Обзор",client:"Клиент",server:"Сервер",database:"База данных",cycle:"Жизненные циклы"};
let data, nodes, current = "overview", engine = "sqlite", clientView = "client", scenario = 0, selected = {}, resize;

function el(tag, label, className) {
  const node = document.createElement(tag);
  if (label !== undefined) node.textContent = label;
  if (className) node.className = className;
  return node;
}
function button(label, action, cls) {
  const b = el("button", label, cls); b.type = "button"; b.onclick = action; return b;
}
function source(pos, label) {
  if (!pos) return el("span", "Участок исходника изменился", "empty");
  const a = el("a", label || (pos.file + " · " + pos.line), "source");
  a.href = "/source?" + new URLSearchParams({file:pos.file, line:pos.line});
  a.target = "_blank"; a.rel = "noopener"; return a;
}
function go(view) {
  current = view;
  history.replaceState(null, "", "#" + view);
  render();
}
function rowDetails(title, entries) {
  const box = el("details");
  box.append(el("summary", title + " · " + entries.length));
  box.ontoggle = () => {
    if (!box.open || box.childElementCount > 1) return;
    for (const e of entries) {
      const row = el("div",undefined,"chunk"); row.append(source(e,e.name)); box.append(row);
    }
  };
  return box;
}
function overview() {
  const flow = el("div",undefined,"flow");
  for (const [view,name,scope,group] of [
    ["client","FFOneUnity","Unity · пространства имён","client"],
    ["server","RustyFusion","Rust · модули","server"],
    ["database","База данных","SQL · таблицы SQLite","sqlite"]
  ]) {
    if (flow.childElementCount) flow.append(el("div",view==="server"?"0104":"SQL","bridge"));
    const node = button("",()=>go(view),"system");
    node.append(el("strong",name),el("span",scope),el("small",data.nodes.filter(n=>n.group===group).length+" в индексе ↗"));
    flow.append(node);
  }
  content.append(el("h2","Системы проекта"),flow,el("h2","Сценарии"));
  const list = el("div",undefined,"scenarios");
  data.scenarios.forEach((s,i)=>list.append(button(s.name+"  →",()=>{scenario=i;go("cycle");},"scenario")));
  content.append(list);
}
function cycles() {
  const head = el("div",undefined,"panelhead"), picker = el("select");
  picker.setAttribute("aria-label","Сценарий");
  data.scenarios.forEach((s,i)=>picker.add(new Option(s.name,i)));
  picker.value=scenario;picker.onchange=()=>{scenario=Number(picker.value);render();};
  head.append(el("h2",data.scenarios[scenario].name),picker);
  content.append(head,el("p","Статический маршрут по исходникам. Ветки ошибок и отмены срабатывают при соответствующих условиях.","caption"));
  const seq=el("ol",undefined,"sequence");
  for(const step of data.scenarios[scenario].steps){
    const li=el("li");li.append(el("strong",step.name),source(step.source));seq.append(li);
  }
  content.append(seq);
}
function linkPaths(canvas, paths, links) {
  const ref = canvas.getBoundingClientRect();
  paths.replaceChildren();
  links.forEach(([from,to])=>{
    const a=from.getBoundingClientRect(), b=to.getBoundingClientRect();
    const x=a.right-ref.left, y=a.top+a.height/2-ref.top;
    const u=b.left-ref.left, v=b.top+b.height/2-ref.top, bend=(x+u)/2;
    const path=document.createElementNS("http://www.w3.org/2000/svg","path");
    path.setAttribute("d", "M "+x+" "+y+" C "+bend+" "+y+" "+bend+" "+v+" "+u+" "+v);
    path.setAttribute("fill","none");path.setAttribute("stroke","#99a2b7");paths.append(path);
  });
}
function groupView(group) {
  const items=data.nodes.filter(n=>n.group===group).sort((a,b)=>a.name.localeCompare(b.name));
  const defaults={client:"cs:FFOne.UI",server:"rs:server::login",sqlite:"sqlite:Players",postgres:"postgres:Players"};
  let id=selected[group]||defaults[group];
  if(!items.some(n=>n.id===id))id=items[0]?.id;
  const head=el("div",undefined,"panelhead"), toggle=el("div",undefined,"switch");
  head.append(el("h2",tabs[current]));
  if(current==="database"){
    for(const [e,label] of [["sqlite","SQLite"],["postgres","PostgreSQL"]])
      toggle.append(button(label,()=>{engine=e;render();},e===engine?"selected":""));
  } else if(current==="client"){
    for(const [e,label] of [["client","Код C#"],["scene","Сцена Bootstrap"]])
      toggle.append(button(label,()=>{clientView=e;render();},e===clientView?"selected":""));
  }
  head.append(toggle);content.append(head);
  const captions={client:"Пространства имён и директивы using из Assets/FFOne/Runtime.",scene:"Компоненты и сериализованные ссылки по fileID в сцене Bootstrap/Main.unity.",server:"Модули библиотечного крейта RustyFusion, декларации mod и явные импорты use crate.",sqlite:"Таблицы и внешние ключи SQLite из базовой схемы и SQL создания таблиц.",postgres:"Таблицы и внешние ключи PostgreSQL из базовой схемы и SQL создания таблиц."};
  content.append(el("p",captions[group],"caption"));
  if(!id){content.append(el("p","В этом разделе нет доступных источников.","empty"));return;}
  const workspace=el("div",undefined,"workspace"),index=el("aside",undefined,"index"),pane=el("div",undefined,"diagram-pane");
  const filter=el("input");filter.type="search";filter.placeholder="Найти узел…";filter.setAttribute("aria-label","Фильтр узлов");
  const list=el("div",undefined,"list");index.append(filter,list);
  const openNode = key => {id=key;selected[group]=key;drawList();drawPane();};
  const drawList=()=>{
    const q=filter.value.toLowerCase(), visible=items.filter(n=>n.name.toLowerCase().includes(q));
    list.replaceChildren(...visible.map(n=>button(n.name,()=>openNode(n.id),n.id===id?"chosen":"")));
    if(!visible.length)list.append(el("p","Нет совпадений","empty"));
  };
  const drawPane=()=>{
    resize?.disconnect();pane.replaceChildren();
    const node=nodes.get(id), incoming=data.edges.filter(e=>e.to===id),outgoing=data.edges.filter(e=>e.from===id);
    const labels=el("div",undefined,"col-labels");
    labels.append(el("span","Входящие"),el("span","Выбранный узел"),el("span","Исходящие"));
    const wrap=el("div",undefined,"canvas-wrap"), graph=el("div",undefined,"canvas");
    const paths=document.createElementNS("http://www.w3.org/2000/svg","svg");
    paths.setAttribute("aria-hidden","true");const middle=el("div",node.name,"node center"),connect=[];
    function stack(edges, direction){
      const group=el("div",undefined,"stack");
      for(const edge of edges){
        const peer=nodes.get(direction==="in"?edge.from:edge.to);
        const item=button(peer.name,()=>openNode(peer.id),"node");
        item.append(el("small",edge.label));group.append(item);
        connect.push(direction==="in"?[item,middle]:[middle,item]);
      }
      return group;
    }
    graph.append(paths,stack(incoming,"in"),middle,stack(outgoing,"out"));wrap.append(graph);pane.append(labels,wrap);
    const inspector=el("div",undefined,"inspection");inspector.append(el("h3",node.name),source(node.source));
    const refs=[];
    for(const edge of [...incoming,...outgoing]){
      for(const pos of edge.sources){
        refs.push({...pos,name:nodes.get(edge.from).name+" → "+nodes.get(edge.to).name+" · "+edge.label+" · "+pos.file.split("/").pop()+":"+pos.line});
      }
    }
    inspector.append(rowDetails("Примеры связей в исходниках",refs),rowDetails(current==="database"?"Поля и ограничения":group==="scene"?"Тип компонента":"Файлы",node.items));
    if(node.queries)inspector.append(rowDetails("SQL с обращением к таблице",node.queries));
    pane.append(inspector);
    resize=new ResizeObserver(()=>linkPaths(graph,paths,connect));resize.observe(graph);
    requestAnimationFrame(()=>linkPaths(graph,paths,connect));
  };
  filter.oninput=drawList;workspace.append(index,pane);content.append(workspace);drawList();drawPane();
}
function render() {
  resize?.disconnect();content.replaceChildren();const nav=document.querySelector("#tabs");
  nav.replaceChildren(...Object.entries(tabs).map(([k,v])=>{const b=button(v,()=>go(k));b.setAttribute("aria-current",String(k===current));return b;}));
  if(current==="overview")overview();else if(current==="cycle")cycles();else groupView(current==="database"?engine:current==="client"?clientView:current);
}
async function load() {
  const reload=document.querySelector("#reload");reload.disabled=true;notice.textContent="Чтение исходников…";
  try{
    const resp=await fetch("/api/architecture",{cache:"no-store"});if(!resp.ok)throw Error("HTTP "+resp.status);
    data=await resp.json();nodes=new Map(data.nodes.map(n=>[n.id,n]));
    notice.textContent=data.missing.length?"Недоступные источники: "+data.missing.join(", "):"";
    const hash=location.hash.slice(1);current=Object.hasOwn(tabs,hash)?hash:"overview";render();
  }catch(err){notice.textContent="Не удалось обновить архитектуру. "+err.message;}
  finally{reload.disabled=false;}
}
document.querySelector("#reload").onclick=load;
load();
