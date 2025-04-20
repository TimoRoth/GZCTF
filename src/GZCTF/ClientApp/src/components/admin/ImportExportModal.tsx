import { Button, Modal, ModalProps, Stack, Textarea, Group } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiContentCopy, mdiSend, mdiFileUpload, mdiContentSave } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState, useRef, ChangeEvent } from 'react'
import { useTranslation } from 'react-i18next'

type Mode = 'import' | 'export'

interface ImportExportModalProps extends ModalProps {
  mode: Mode
  onSubmitCB?: (text: string) => Promise<unknown> | void
  title: string
  data?: string
}

export const ImportExportModal: FC<ImportExportModalProps> = (props) => {
  const { mode, onSubmitCB, title, data = '', ...modalProps } = props
  const [text, setText] = useInputState(data)
  const [disabled, setDisabled] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const downloadLinkRef = useRef<HTMLAnchorElement>(null)

  const { t } = useTranslation()

  const onSend = async () => {
    if (!onSubmitCB) return
    setDisabled(true)
    try {
      await onSubmitCB(text)
    } catch {
      showNotification({
        color: 'red',
        message: t('common.error.encountered'),
      })
    } finally {
      setDisabled(false)
    }
  }

  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      showNotification({
        color: 'teal',
        message: t('common.messages.copied_to_clipboard'),
        icon: <Icon path={mdiContentCopy} size={1} />,
      })
    } catch (e) {
      showNotification({
        color: 'red',
        message: t('common.error.encountered'),
      })
    }
  }

  const onFileChange = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file)
      return;

    const reader = new FileReader()
    reader.onload = (fe) => {
      setText(fe.target?.result as string || '')
    }
    reader.readAsText(file)

    e.target.value = ''
  }

  const onSaveAs = () => {
    if (!downloadLinkRef.current)
      return;

    const blob = new Blob([text], { type: 'text/plain' })
    const url = URL.createObjectURL(blob)

    downloadLinkRef.current.href = url
    downloadLinkRef.current.click()

    setTimeout(() => {
      URL.revokeObjectURL(url)
      if (downloadLinkRef.current)
        downloadLinkRef.current.href = ''
    }, 0)
  }

  return (
    <Modal size="lg" title={title} {...modalProps}>
      <Stack>
        <Textarea
          autosize
          minRows={8}
          maxRows={18}
          value={text}
          onChange={setText}
          readOnly={mode === 'export'}
          w="100%"
        />
        <Group grow>
          {mode === 'import' && (
            <>
              <input
                type="file"
                ref={fileInputRef}
                style={{ display: 'none' }}
                accept=".yml,.yaml,.json,.txt,text/*"
                onChange={onFileChange}
              />
              <Button
                leftSection={<Icon path={mdiFileUpload} size={1} />}
                variant="default"
                disabled={disabled}
                onClick={() => fileInputRef.current?.click()}
              >
                {t('common.button.select_file')}
              </Button>
              <Button
                fullWidth
                leftSection={<Icon path={mdiSend} size={1} />}
                disabled={disabled}
                onClick={onSend}
              >
                {t('common.button.submit')}
              </Button>
            </>
          )}
          {mode === 'export' && (
            <>
              <a
                style={{ display: 'none' }}
                ref={downloadLinkRef}
                download="export.yml"
              >
              </a>
              <Button
                leftSection={<Icon path={mdiContentSave} size={1} />}
                variant="default"
                onClick={onSaveAs}
              >
                {t('common.button.download')}
              </Button>
              <Button
                fullWidth
                leftSection={<Icon path={mdiContentCopy} size={1} />}
                disabled={disabled}
                onClick={onCopy}
              >
                {t('common.button.copy')}
              </Button>
            </>
          )}
        </Group>
      </Stack>
    </Modal>
  )
}
