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
})();
