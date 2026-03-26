"use client"

import { useState, useEffect } from "react"
import { DeleteTemplate, ViewTemplate, extractApiErrors } from "@sseta/components"
import { useAccessStaffRoleRequest } from "@/contexts/resources/access/AccessStaffRoleRequestContext"
import { useToast } from "@/contexts/general/ToastContext"
import AccessStaffRoleRequestViewLayout from "../view/AccessStaffRoleRequestViewLayout"

interface AccessStaffRoleRequestDeleteFormProps {
  staffRoleRequestId: number
  hiddenFields?: string[]
  renderActionsInFooter?: boolean
  className?: string
  loading?: boolean
  onDeleted?: (staffRoleRequestId: number) => void
}

export default function AccessStaffRoleRequestDeleteForm(props: AccessStaffRoleRequestDeleteFormProps) {
  const {
    staffRoleRequestId,
    hiddenFields,
    renderActionsInFooter = true,
    className = "px-6 py-4",
    loading: loadingOverride,
    onDeleted,
  } = props

  const [record, setRecord] = useState<any>(null)
  const [loading, setLoading] = useState(false)
  const [apiError, setApiError] = useState<string | undefined>()
  const isLoading = loadingOverride ?? loading

  const { retrieve, destroy } = useAccessStaffRoleRequest()
  const { showToast } = useToast()

  useEffect(() => {
    const fetchRecord = async () => {
      setLoading(true)
      try {
        const result = await retrieve(staffRoleRequestId)
        if (result) setRecord(result)
      } catch (error) {
        console.error("Failed to fetch staff role request:", error)
      } finally {
        setLoading(false)
      }
    }
    fetchRecord()
  }, [])

  const onDelete = async () => {
    setLoading(true)
    setApiError(undefined)
    try {
      await destroy(staffRoleRequestId)
      showToast("Staff Role Request successfully deleted", "success")
      onDeleted?.(staffRoleRequestId)
    } catch (error: any) {
      setApiError(extractApiErrors(error)[0])
    } finally {
      setLoading(false)
    }
  }

  return (
    <DeleteTemplate
      entityName="Staff Role Request"
      onDelete={onDelete}
      renderActionsInFooter={renderActionsInFooter}
      loading={isLoading}
      errorMessage={apiError}
      className={className}
    >
      <ViewTemplate
        layout={AccessStaffRoleRequestViewLayout}
        record={record}
        isLoading={isLoading}
        hiddenFields={hiddenFields}
      />
    </DeleteTemplate>
  )
}
