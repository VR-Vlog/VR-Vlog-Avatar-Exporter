(() => {
const dialog=document.querySelector('#tutorial-dialog'), video=document.querySelector('#tutorial-video'), title=document.querySelector('#tutorial-title');
let opener;
let requestId=0;
document.querySelectorAll('[data-video]').forEach(button=>button.addEventListener('click',()=>{
 opener=button; title.textContent=button.dataset.title; dialog.showModal();
 const start=Number(button.dataset.video), currentRequest=++requestId;
 const play=()=>{if(!dialog.open||currentRequest!==requestId)return;video.currentTime=start;video.play().catch(()=>{});};
 if(!video.getAttribute('src')){video.src=video.dataset.src;video.addEventListener('loadedmetadata',play,{once:true});video.load();}else if(video.readyState>=1){play();}else{video.addEventListener('loadedmetadata',play,{once:true});}
}));
dialog?.querySelector('.dialog-close').addEventListener('click',()=>dialog.close());
dialog?.addEventListener('close',()=>{requestId++;video.pause();opener?.focus({preventScroll:true});});
const links=[...document.querySelectorAll('.doc-sidebar nav a')];
if(links.length){const observer=new IntersectionObserver(entries=>{const entry=entries.filter(e=>e.isIntersecting).sort((a,b)=>a.boundingClientRect.top-b.boundingClientRect.top)[0];if(!entry)return;links.forEach(a=>{if(a.hash==='#'+entry.target.id)a.setAttribute('aria-current','true');else a.removeAttribute('aria-current');});},{rootMargin:'-8% 0px -65% 0px'});document.querySelectorAll('main section[id]').forEach(s=>observer.observe(s));}
})();
const tocToggle=document.querySelector('.toc-toggle');tocToggle?.addEventListener('click',()=>{const expanded=tocToggle.getAttribute('aria-expanded')!=='true';tocToggle.setAttribute('aria-expanded',String(expanded));tocToggle.parentElement.classList.toggle('is-open',expanded);tocToggle.querySelector('span').textContent=expanded?'−':'＋';});
